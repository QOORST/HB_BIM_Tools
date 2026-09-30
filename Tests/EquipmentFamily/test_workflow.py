import copy
import importlib.util
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'Tools/EquipmentFamily'))
import workflow as w

class WorkflowTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.root=Path(self.temp.name)
        from PIL import Image
        self.image=self.root/'設備.png';Image.new('RGB',(100,60),'white').save(self.image)
        self.ocr=patch.object(w,'ocr',return_value={'status':'extracted_unreviewed','text':'TOS-EF-05 496 mm','language':'zh-Hant-TW'});self.ocr.start()
    def tearDown(self):self.ocr.stop();self.temp.cleanup()
    def case(self,category='pump',target=2024):
        return w.new_case(category,[self.image],self.root/('case-'+w.uuid.uuid4().hex[:5]),target)
    def ready(self):
        case=self.case();w.set_recipe(case,w.RECIPE);w.review(case,'測試覆核者','已依來源核對兩個型號',True);return case
    def test_ocr_does_not_become_geometry(self):
        case=self.case();m=w.read(case/'case.json')
        self.assertIsNone(m['recipe']);self.assertFalse(w.validate(case)['can_build'])
        data=w.read(case/m['sources'][0]['analysis']);self.assertEqual(data['pages'][0]['clues'][0]['review'],'unreviewed')
    def test_ahu_requires_matching_catalog(self):
        case=self.case('ahu');w.set_recipe(case,w.AHU_RECIPE)
        self.assertTrue(any('原檔' in x for x in w.validate(case,False)['errors']))
    def test_ahu_rejects_pump_template(self):
        with self.assertRaises(ValueError):w.set_recipe(self.case('ahu'),w.RECIPE)
    def test_ahu_packet_routes_and_freezes_review(self):
        case=self.case('ahu');reference=w.read(w.AHU_RECIPE)
        reference['source_sha256']=w.digest(self.image)
        fixture=self.root/'ahu-reference.json';w.write(fixture,reference)
        with patch.object(w,'AHU_RECIPE',fixture):
            w.set_recipe(case,fixture)
            self.assertFalse(w.validate(case)['can_build'])
            w.review(case,'開發測試','已核對尺寸及接頭假設',True)
            packet=w.prepare(case);self.assertEqual(w.read(packet)['template_id'],w.AHU_TEMPLATE)
            m=w.read(case/'case.json');m['recipe']['types'][0]['C']=999;w.write(case/'case.json',m)
            errors=w.validate(case)['errors']
            self.assertTrue(any('配置' in x for x in errors));self.assertTrue(any('變更' in x for x in errors))
            self.assertEqual(w.read(packet.parent/'recipe.json')['types'][0]['C'],1400)
    def test_original_may_move_after_snapshot(self):
        case=self.ready();self.image.unlink();self.assertTrue(w.validate(case)['can_build'])
    def test_tampered_source_blocks_build(self):
        case=self.ready();m=w.read(case/'case.json');(case/m['sources'][0]['snapshot']).write_bytes(b'changed')
        self.assertFalse(w.validate(case)['can_build'])
        with self.assertRaises(ValueError):w.prepare(case)
    def test_review_is_required(self):
        case=self.case();w.set_recipe(case,w.RECIPE)
        with self.assertRaises(ValueError):w.prepare(case)
    def test_recipe_edit_invalidates_review(self):
        case=self.ready();m=w.read(case/'case.json');m['recipe']['types'][0]['assembly_dimensions']['H']=999;w.write(case/'case.json',m)
        errors=w.validate(case)['errors'];self.assertTrue(any('變更' in e for e in errors));self.assertTrue(any('配置' in e for e in errors))
    def test_unsupported_category_not_fake_success(self):
        case=self.case('fan')
        with self.assertRaises(ValueError):w.set_recipe(case,w.RECIPE)
        self.assertFalse(w.validate(case)['can_build'])
    def test_unvalidated_version_is_blocked(self):
        case=self.case(target=2026);w.set_recipe(case,w.RECIPE)
        self.assertFalse(w.validate(case)['can_build'])
    def test_unknown_or_duplicate_model_is_blocked(self):
        case=self.ready();m=w.read(case/'case.json');m['recipe']['types'][1]=copy.deepcopy(m['recipe']['types'][0]);w.write(case/'case.json',m)
        self.assertFalse(w.validate(case)['can_build'])
    def test_mixed_units_blocked(self):
        case=self.ready();m=w.read(case/'case.json');m['recipe']['units']['length']='inch';w.write(case/'case.json',m)
        self.assertFalse(w.validate(case)['can_build'])
    def test_packet_freezes_review_and_sources(self):
        case=self.ready();path=w.prepare(case);r=w.read(path)
        self.assertEqual(r['recipe_sha256'],w.digest(path.parent/r['recipe_file']))
        self.assertEqual(r['source_receipts'][0]['sha256'],w.digest(path.parent/r['source_receipts'][0]['file']))
        before=(path.parent/r['recipe_file']).read_bytes()
        m=w.read(case/'case.json');m['recipe']['status']='changed';w.write(case/'case.json',m)
        self.assertEqual(before,(path.parent/r['recipe_file']).read_bytes())
    def test_path_traversal_is_rejected(self):
        with self.assertRaises(ValueError):w.local(self.root,'../other.json')
    def test_neutral_3d_is_not_reported_as_parsed(self):
        source=self.root/'equipment.step';source.write_text('ISO-10303-21;')
        case=w.new_case('pump',[source],self.root/'step');w.set_recipe(case,w.RECIPE)
        self.assertFalse(w.validate(case,False)['can_build'])
    def test_report_escapes_untrusted_text(self):
        case=self.case();m=w.read(case/'case.json');p=case/m['sources'][0]['analysis'];data=w.read(p)
        data['pages'][0]['text']='<script>alert(1)</script>';w.write(p,data);w.report(case)
        text=(case/'report.html').read_text(encoding='utf-8');self.assertNotIn('<script>',text);self.assertIn('&lt;script&gt;',text)
    def test_pdf_scan_renders_and_uses_ocr(self):
        from pypdf import PdfWriter
        writer=PdfWriter();writer.add_blank_page(width=300,height=300)
        source=self.root/'scan.pdf'
        with source.open('wb') as f:writer.write(f)
        case=w.new_case('pump',[source],self.root/'pdf');m=w.read(case/'case.json');data=w.read(case/m['sources'][0]['analysis'])
        self.assertEqual(data['pages'][0]['method'],'ocr');self.assertTrue((case/'evidence/s001/page-1.png').is_file())
    def test_ocr_failure_preserves_source_for_manual_review(self):
        with patch.object(w,'ocr',return_value={'status':'unavailable','reason':'No OCR language'}):case=self.case()
        m=w.read(case/'case.json');self.assertEqual(m['sources'][0]['status'],'needs_review');self.assertTrue((case/m['sources'][0]['snapshot']).is_file())
    def test_empty_ocr_requires_manual_review(self):
        with patch.object(w,'ocr',return_value={'status':'extracted_unreviewed','text':''}):case=self.case()
        self.assertEqual(w.read(case/'case.json')['sources'][0]['status'],'needs_review')
    def test_unsupported_installation_is_not_silently_ignored(self):
        case=self.ready();m=w.read(case/'case.json');m['recipe']['installation']['guide_rail_length']=1800;w.write(case/'case.json',m)
        self.assertFalse(w.validate(case,False)['can_build'])
    def test_embedded_pdf_text_avoids_ocr(self):
        from pypdf import PdfWriter
        from pypdf.generic import DictionaryObject,NameObject,DecodedStreamObject
        writer=PdfWriter();page=writer.add_blank_page(width=300,height=300)
        font=DictionaryObject({NameObject('/Type'):NameObject('/Font'),NameObject('/Subtype'):NameObject('/Type1'),NameObject('/BaseFont'):NameObject('/Helvetica')})
        page[NameObject('/Resources')]=DictionaryObject({NameObject('/Font'):DictionaryObject({NameObject('/F1'):font})})
        stream=DecodedStreamObject();stream.set_data(b'BT /F1 12 Tf 10 200 Td (PUMP-DEMO-01 width 300 mm height 500 mm) Tj ET')
        page[NameObject('/Contents')]=writer._add_object(stream)
        source=self.root/'vector.pdf'
        with source.open('wb') as f:writer.write(f)
        with patch.object(w,'ocr',side_effect=AssertionError('OCR should not run')):
            case=w.new_case('pump',[source],self.root/'vector-case')
        m=w.read(case/'case.json');data=w.read(case/m['sources'][0]['analysis'])
        self.assertEqual(data['pages'][0]['method'],'embedded_text');self.assertIn('300 mm',data['pages'][0]['text'])

if __name__=='__main__':unittest.main()
