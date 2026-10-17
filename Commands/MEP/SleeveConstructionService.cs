using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using YD_RevitTools.LicenseManager.Helpers;

namespace YD_RevitTools.LicenseManager.Commands.MEP
{
    internal sealed class SleeveConstructionRequest
    {
        internal string Name, Revision, Zone;
        internal ElementId SourceViewId, ScopeLevelId, GridAId, GridBId, TitleBlockId;
    }
    internal static class SleeveConstructionService
    {
        private static readonly Guid SchemaId = new Guid("3d28fbd1-960a-4217-8d1f-6e668eb1982e");
        private const double Mm = 1.0 / 304.8;
        internal static List<SleeveConstructionPackage> List(Document doc) => SleeveDocumentStorage.Find(doc,SchemaId)
            .Select(s=>SleeveDocumentStorage.Read<SleeveConstructionPackage>(s,SchemaId)).OrderByDescending(p=>p.UpdatedUtc).ToList();
        private static void Store(Document doc, SleeveConstructionPackage p)
        {
            var matches=SleeveDocumentStorage.Find(doc,SchemaId).Where(s=>SleeveDocumentStorage.Read<SleeveConstructionPackage>(s,SchemaId).Id==p.Id).ToList();
            if(matches.Count>1) throw new InvalidOperationException("施工包識別重複，請檢查模型。");
            p.UpdatedUtc=DateTime.UtcNow;
            SleeveDocumentStorage.Write(doc,SchemaId,p,matches.SingleOrDefault());
        }
        internal static SleeveConstructionPackage Preview(Document doc, SleeveConstructionRequest request)
        {
            if(request?.SourceViewId==null||request.ScopeLevelId==null||request.GridAId==null||request.GridBId==null)
                throw new InvalidOperationException("請完整選擇來源平面圖、約束樓層與兩條定位軸線。");
            var view=doc.GetElement(request.SourceViewId) as ViewPlan;
            if(view==null||view.IsTemplate||view.GenLevel==null||!view.CanViewBeDuplicated(ViewDuplicateOption.WithDetailing))
                throw new InvalidOperationException("請選擇可複製含詳圖的樓層平面圖。");
            if(List(doc).Any(p=>p.PlanId==view.UniqueId))
                throw new InvalidOperationException("請選擇原始平面圖，不能將施工包產生的定位圖再次作為來源。");
            var level=doc.GetElement(request.ScopeLevelId) as Level;
            if(level==null) throw new InvalidOperationException("請指定套管統計的約束樓層；統一約束 GL 時請另填施工區域。");
            var a=doc.GetElement(request.GridAId) as Grid; var b=doc.GetElement(request.GridBId) as Grid;
            if(!(a?.Curve is Line la)||!(b?.Curve is Line lb)||a.Id==b.Id)
                throw new InvalidOperationException("請指定兩條不同的直線軸線：A 軸在圖面上下延伸，B 軸在圖面左右延伸。");
            if(Math.Abs(la.Direction.DotProduct(view.UpDirection))<1-1e-8||Math.Abs(lb.Direction.DotProduct(view.RightDirection))<1-1e-8)
                throw new InvalidOperationException("定位軸線須與平面圖方向一致：A 軸平行圖面上下方向、B 軸平行圖面左右方向。");
            var options=new SleeveQuantityOptions{LevelKey=level.UniqueId,Zone=(request.Zone??"").Trim()};
            var rows=SleeveQuantityData.Read(doc).Where(options.Matches).ToList();
            var visible=new HashSet<long>(new FilteredElementCollector(doc,view.Id).OfClass(typeof(FamilyInstance)).ToElementIds().Select(id=>id.GetIdValue()));
            var missing=rows.Where(r=>!visible.Contains(r.Id.GetIdValue())).ToList();
            if(missing.Count>0) throw new InvalidOperationException("所選樓層／區域有 "+missing.Count+" 支套管不在來源平面圖可見範圍；請調整視圖或區域，避免圖表漏件。例："+string.Join("、",missing.Take(5).Select(r=>r.Mark)));
            var p=new SleeveConstructionPackage{Name=(request.Name??"").Trim(),Revision=(request.Revision??"").Trim(),SourceViewId=view.UniqueId,SourceViewName=view.Name,
                GridAId=a.UniqueId,GridBId=b.UniqueId,GridAName=a.Name,GridBName=b.Name,LevelKey=level.UniqueId,LevelName=level.Name,Zone=options.Zone,
                DatumName=view.GenLevel.Name,DatumElevation=view.GenLevel.ProjectElevation/Mm};
            var originA=la.GetEndPoint(0);var originB=lb.GetEndPoint(0);
            p.GridFrame=new[]{originA.X,originA.Y,originA.Z,view.RightDirection.X,view.RightDirection.Y,view.RightDirection.Z,
                originB.X,originB.Y,originB.Z,view.UpDirection.X,view.UpDirection.Y,view.UpDirection.Z};
            foreach(var r in rows.OrderBy(r=>r.Mark,StringComparer.Ordinal))
            {
                var fi=doc.GetElement(r.Id) as FamilyInstance; var box=fi?.get_BoundingBox(null);
                if(box==null)throw new InvalidOperationException("無法取得套管 "+r.Mark+" 的實體中心。");
                var point=box.Transform.OfPoint((box.Min+box.Max)*.5);
                var transform=fi.GetTransform();
                p.Items.Add(new SleeveConstructionItem{Id=fi.UniqueId,Mark=r.Mark,Specification=r.Specification,Length=r.Length,System=r.System,Host=r.Host,Check=r.Status,
                    X=point.X/Mm,Y=point.Y/Mm,Z=point.Z/Mm,OffsetA=(point-originA).DotProduct(view.RightDirection)/Mm,
                    OffsetB=(point-originB).DotProduct(view.UpDirection)/Mm,Elevation=point.Z/Mm-p.DatumElevation,
                    Orientation=new[]{transform.BasisX.X,transform.BasisX.Y,transform.BasisX.Z,transform.BasisY.X,transform.BasisY.Y,transform.BasisY.Z,transform.BasisZ.X,transform.BasisZ.Y,transform.BasisZ.Z}});
            }
            SleeveConstructionPolicy.Validate(p);return p;
        }
        private static SleeveConstructionRequest Request(Document doc,SleeveConstructionPackage p) => new SleeveConstructionRequest{Name=p.Name,Revision=p.Revision,Zone=p.Zone,
            SourceViewId=doc.GetElement(p.SourceViewId)?.Id,ScopeLevelId=doc.GetElement(p.LevelKey)?.Id,GridAId=doc.GetElement(p.GridAId)?.Id,GridBId=doc.GetElement(p.GridBId)?.Id};
        internal static List<string> Impacts(Document doc,SleeveConstructionPackage p)
        {
            var now=Preview(doc,Request(doc,p));var result=new List<string>();
            if(now.GridAName!=p.GridAName||now.GridBName!=p.GridBName||now.DatumName!=p.DatumName||Math.Abs(now.DatumElevation-p.DatumElevation)>.5||
                now.GridFrame.Zip(p.GridFrame,(a,b)=>Math.Abs(a-b)).Any(d=>d>1e-8))result.Add("定位軸線或樓層高程基準已變更，需重新核對全部定位資料。");
            foreach(var old in p.Items)
            {
                string change=SleeveConstructionPolicy.Changed(old,now.Items.SingleOrDefault(i=>i.Id==old.Id));
                var field=p.FieldRecords.SingleOrDefault(r=>r.ElementId==old.Id);
                if(change.Length>0)result.Add(old.Mark+"："+change+(field?.Installation=="已安裝"?"；已安裝，須現場確認":""));
            }
            result.AddRange(now.Items.Where(i=>p.Items.All(o=>o.Id!=i.Id)).Select(i=>i.Mark+"：新增"));
            return result;
        }
        internal static SleeveConstructionPackage CreateOrUpdate(Document doc,SleeveConstructionRequest request)
        {
            var p=Preview(doc,request);
            var matches=List(doc).Where(x=>string.Equals(x.Name,p.Name,StringComparison.OrdinalIgnoreCase)&&string.Equals(x.Revision,p.Revision,StringComparison.OrdinalIgnoreCase)).ToList();
            if(matches.Count>1)throw new InvalidOperationException("同名同版施工包重複。");
            var old=matches.SingleOrDefault();
            if(old!=null && old.Status!="草稿")throw new InvalidOperationException("已核准或作廢的版次不可覆寫，請建立新版。");
            if(old!=null && (old.SourceViewId!=p.SourceViewId||old.GridAId!=p.GridAId||old.GridBId!=p.GridBId||old.LevelKey!=p.LevelKey||old.Zone!=p.Zone))
                throw new InvalidOperationException("同版更新不可更換來源圖、範圍或定位軸線，請建立新版。");
            if(old!=null && old.FieldRecords.Any(f=>f.Installation!="未施工"))
                throw new InvalidOperationException("此版已有現場施工紀錄，請建立新版並保留原始查驗依據。");
            var title=request.TitleBlockId==null?null:doc.GetElement(request.TitleBlockId) as FamilySymbol;
            if(old==null && (title==null||title.Category.Id.GetIdValue()!=(long)BuiltInCategory.OST_TitleBlocks))
                throw new InvalidOperationException("請選擇已載入的圖框型別。");
            using(var tx=new Transaction(doc,"套管施工定位與查驗包"))
            {
                tx.Start();
                var source=(ViewPlan)doc.GetElement(request.SourceViewId);
                ViewPlan plan;
                if(old==null)
                {
                    plan=(ViewPlan)doc.GetElement(source.Duplicate(ViewDuplicateOption.WithDetailing));
                    plan.Name=UniqueName(doc,"HB_套管定位_"+p.Name+"_"+p.Revision);
                    p.PlanId=plan.UniqueId;
                    p.SheetIds.Add(NewSheet(doc,title,p,plan,1).UniqueId);
                }
                else
                {
                    p.Id=old.Id;p.PlanId=old.PlanId;p.HeaderId=old.HeaderId;p.HeaderText=old.HeaderText;
                    p.SheetIds=old.SheetIds;p.TableIds=old.TableIds;p.FieldRecords=old.FieldRecords;
                    plan=doc.GetElement(p.PlanId) as ViewPlan ?? throw new InvalidOperationException("定位圖已刪除，請建立新版。");
                    // Existing paper layout and manual annotations are never deleted/recreated.
                    foreach(var item in old.Items)
                    {
                        var note=doc.GetElement(item.NoteId) as TextNote;
                        if(note==null||note.OwnerViewId!=plan.Id||Clean(note.Text)!=Clean(item.NoteText))
                            throw new InvalidOperationException("套管 "+item.Mark+" 的自動標註已被手動修改或刪除，請建立新版以保留人工成果。");
                        var next=p.Items.SingleOrDefault(i=>i.Id==item.Id);
                        if(next==null) { doc.Delete(note.Id);continue; }
                        next.NoteId=note.UniqueId;
                    }
                }
                ElementId textType=TextType(doc);
                foreach(var item in p.Items)
                {
                    string text=item.Mark+"  "+item.Specification+"\n距 "+p.GridAName+" 軸 "+N(item.OffsetA)+"／距 "+p.GridBName+" 軸 "+N(item.OffsetB)+" mm\n中心 EL "+N(item.Elevation)+" mm";
                    var point=new XYZ(item.X*Mm,item.Y*Mm,source.GenLevel.ProjectElevation);
                    TextNote note=string.IsNullOrEmpty(item.NoteId)?null:doc.GetElement(item.NoteId) as TextNote;
                    if(note==null)
                    {
                        note=TextNote.Create(doc,plan.Id,point+plan.RightDirection*plan.Scale*6*Mm+plan.UpDirection*plan.Scale*6*Mm,
                            52*Mm,text,new TextNoteOptions(textType));
                        note.AddLeader(TextNoteLeaderTypes.TNLT_STRAIGHT_L).End=point;
                    }
                    else { note.Text=text;foreach(var leader in note.GetLeaders())leader.End=point; }
                    item.NoteId=note.UniqueId;item.NoteText=text;
                }
                UpdateHeader(doc,p,plan,textType);
                // Fixed per-revision table pages; update only text owned by this package.
                UpdateTables(doc,p,old,title??GetTitle(doc,p),textType);
                doc.Regenerate();
                if(old==null)
                    foreach(var sheet in p.SheetIds.Select(id=>doc.GetElement(id) as ViewSheet))
                        foreach(var viewport in new FilteredElementCollector(doc,sheet.Id).OfClass(typeof(Viewport)).Cast<Viewport>())
                            viewport.SetBoxCenter(new XYZ((sheet.Outline.Min.U+sheet.Outline.Max.U)/2,(sheet.Outline.Min.V+sheet.Outline.Max.V)/2,0));
                doc.Regenerate();
                foreach(string id in p.SheetIds) CheckSheet(doc,(ViewSheet)doc.GetElement(id));
                Store(doc,p);
                if(tx.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("施工包交易未提交。");
            }
            return p;
        }
        private static string Clean(string s)=>(s??"").Replace("\r\n","\n").Replace("\r","\n").TrimEnd('\n');
        private static string N(double n)=>SleeveConstructionPolicy.Number(n);
        private static string UniqueName(Document doc,string name)
        {
            if(!NamingUtils.IsValidName(name))throw new InvalidOperationException("名稱含 Revit 不允許的字元。");
            var used=new HashSet<string>(new FilteredElementCollector(doc).OfClass(typeof(View)).Select(v=>v.Name));
            string value=name;for(int i=2;used.Contains(value);i++)value=name+"_"+i;return value;
        }
        private static ElementId TextType(Document doc)
        {
            const string name="HB_套管施工包_2mm";
            var all=new FilteredElementCollector(doc).OfClass(typeof(TextNoteType)).Cast<TextNoteType>().ToList();
            var t=all.FirstOrDefault(x=>x.Name==name);
            if(t==null){t=(TextNoteType)all.First().Duplicate(name);t.get_Parameter(BuiltInParameter.TEXT_SIZE).Set(2*Mm);}
            return t.Id;
        }
        private static void UpdateHeader(Document doc,SleeveConstructionPackage p,ViewPlan plan,ElementId textType)
        {
            string text=p.Name+"｜"+p.Revision+"｜"+p.Status+"｜"+p.Items.Count+" 支\n"+
                "定位：距 "+p.GridAName+" 軸向圖面右為正；距 "+p.GridBName+" 軸向圖面上為正。\n"+
                "中心標高相對 "+p.DatumName+"；尺寸單位 mm。圖面未標示的構造詳圖需另行補足。\n"+
                (p.Status=="作廢"?"本版已作廢，僅供追溯。" : p.Status=="核准施工"?"人工核准："+p.ApprovedBy+"／依據："+p.ApprovalReference:"草稿供核對；需確認標註重疊、剖面與施工詳圖後辦理核准。");
            var note=string.IsNullOrEmpty(p.HeaderId)?null:doc.GetElement(p.HeaderId) as TextNote;
            if(!string.IsNullOrEmpty(p.HeaderId)&&(note==null||Clean(note.Text)!=Clean(p.HeaderText)))throw new InvalidOperationException("施工包標題已被修改或刪除，保留人工成果並停止更新。");
            if(note==null)
            {
                var box=plan.CropBox;var position=box.Transform.OfPoint(new XYZ(box.Min.X,box.Max.Y,0));
                position=new XYZ(position.X,position.Y,plan.GenLevel.ProjectElevation);
                note=TextNote.Create(doc,plan.Id,position,140*Mm,text,new TextNoteOptions(textType));
            }
            else note.Text=text;
            p.HeaderId=note.UniqueId;p.HeaderText=text;
        }
        private static FamilySymbol GetTitle(Document doc,SleeveConstructionPackage p)
        {
            var sheet=doc.GetElement(p.SheetIds.First()) as ViewSheet;
            return new FilteredElementCollector(doc,sheet.Id).OfCategory(BuiltInCategory.OST_TitleBlocks).OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>().Select(f=>f.Symbol).First();
        }
        private static ViewSheet NewSheet(Document doc,FamilySymbol title,SleeveConstructionPackage p,View view,int index)
        {
            var sheet=ViewSheet.Create(doc,title.Id);
            string number="HB-SL-"+p.Id.Substring(0,8)+"-"+index.ToString("D2");
            sheet.SheetNumber=number;sheet.Name=p.Name+"_"+p.Revision+(index==1?"_定位圖":"_數量查驗表")+"_"+p.Status;
            doc.Regenerate();
            var outline=sheet.Outline;
            Viewport.Create(doc,sheet.Id,view.Id,new XYZ((outline.Min.U+outline.Max.U)/2,(outline.Min.V+outline.Max.V)/2,0));
            return sheet;
        }
        private static void CheckSheet(Document doc,ViewSheet sheet)
        {
            if(sheet==null)throw new InvalidOperationException("施工包圖紙已被刪除。");
            var outline=sheet.Outline;
            foreach(var v in new FilteredElementCollector(doc,sheet.Id).OfClass(typeof(Viewport)).Cast<Viewport>())
            {
                var box=v.GetBoxOutline();
                if(box.MinimumPoint.X<outline.Min.U||box.MaximumPoint.X>outline.Max.U||box.MinimumPoint.Y<outline.Min.V||box.MaximumPoint.Y>outline.Max.V)
                    throw new InvalidOperationException("定位圖或查驗表超出圖框，請先調整來源圖比例／裁剪，或使用較大圖框；本次已回復。");
            }
        }
        private static void UpdateTables(Document doc,SleeveConstructionPackage p,SleeveConstructionPackage old,FamilySymbol title,ElementId textType)
        {
            int pages=(p.Items.Count+11)/12;
            if(old!=null && pages!=old.TableIds.Count)throw new InvalidOperationException("套管數量改變造成查驗表頁數不同，請建立新版以保留原版面。");
            var type=new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().First(t=>t.ViewFamily==ViewFamily.Drafting);
            for(int page=0;page<pages;page++)
            {
                ViewDrafting table;
                if(old==null){table=ViewDrafting.Create(doc,type.Id);table.Scale=1;table.Name=UniqueName(doc,"HB_套管查驗_"+p.Name+"_"+p.Revision+"_"+(page+1));p.TableIds.Add(table.UniqueId);doc.Regenerate();}
                else table=doc.GetElement(p.TableIds[page]) as ViewDrafting??throw new InvalidOperationException("查驗表已刪除，請建立新版。");
                // One body note with deterministic lines; users may move it, but altered content is protected by the page snapshot.
                var notes=new FilteredElementCollector(doc,table.Id).OfClass(typeof(TextNote)).Cast<TextNote>().ToList();
                string body=TableText(p,page);
                if(old==null)TextNote.Create(doc,table.Id,XYZ.Zero,270*Mm,body,new TextNoteOptions(textType));
                else
                {
                    var original=TableText(old,page);
                    var owned=notes.Where(n=>Clean(n.Text)==Clean(original)).ToList();
                    if(owned.Count!=1)throw new InvalidOperationException("查驗表內容已人工修改，停止更新以保留原件。");
                    owned[0].Text=body;
                }
                if(old==null)p.SheetIds.Add(NewSheet(doc,title,p,table,p.SheetIds.Count+1).UniqueId);
            }
        }
        private static string TableText(SleeveConstructionPackage p,int page)
        {
            var b=new StringBuilder(p.Name+"｜"+p.Revision+"｜"+p.Status+"｜套管數量與查驗表 第 "+(page+1)+" 頁\n");
            b.AppendLine("整包 "+p.Items.Count+" 支；本頁每列 1 支。中心標高基準："+p.DatumName+"。單位 mm。核准狀態以版次紀錄為準。");
            b.AppendLine("定位正向：距 "+p.GridAName+" 軸向圖面右；距 "+p.GridBName+" 軸向圖面上。\n");
            foreach(var i in p.Items.Skip(page*12).Take(12))
            {
                b.AppendLine(i.Mark+"｜"+i.System+"｜"+i.Specification+"｜長 "+i.Length+"｜"+i.Host+"｜1 支");
                b.AppendLine("設計：A "+N(i.OffsetA)+"／B "+N(i.OffsetB)+"／中心 EL "+N(i.Elevation)+"；檢核："+i.Check);
                b.AppendLine("實測：A __________ B __________ EL __________；施工 □未施工 □已放樣 □已安裝 □已拆除");
                b.AppendLine("查驗 □未查驗 □待改善 □合格；查驗人／日期／照片索引：________________________\n");
            }
            return b.ToString();
        }
        internal static void SaveField(Document doc,string packageId,List<SleeveFieldRecord> records,DateTime expected)
        {
            var p=List(doc).Single(x=>x.Id==packageId);
            if(p.Status=="作廢")throw new InvalidOperationException("作廢版次僅供追溯，不能再登錄施工查驗。");
            if(p.UpdatedUtc!=expected)throw new InvalidOperationException("施工包已被更新，請重新開啟，避免覆寫他人資料。");
            if(records.GroupBy(r=>r.ElementId).Any(g=>g.Count()>1)||records.Any(r=>p.Items.All(i=>i.Id!=r.ElementId)))throw new InvalidOperationException("查驗資料含重複或不屬於此版的構件。");
            foreach(var r in records)SleeveConstructionPolicy.ValidateField(r);
            using(var tx=new Transaction(doc,"記錄套管施工查驗")){tx.Start();p.FieldRecords=records;Store(doc,p);if(tx.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("查驗資料未提交。");}
        }
        internal static void Approve(Document doc,string id,string person,string reference)
        {
            var p=List(doc).Single(x=>x.Id==id);
            VerifyDrawing(doc,p);
            var impacts=Impacts(doc,p);if(impacts.Count>0)throw new InvalidOperationException("模型已變更，請先更新草稿：\n"+string.Join("\n",impacts.Take(8)));
            var old=JsonConvert.DeserializeObject<SleeveConstructionPackage>(JsonConvert.SerializeObject(p));
            SleeveConstructionPolicy.Approve(p,person,reference);
            using(var tx=new Transaction(doc,"登錄套管施工包人工核准"))
            {
                tx.Start();UpdateHeader(doc,p,(ViewPlan)doc.GetElement(p.PlanId),TextType(doc));
                UpdateTables(doc,p,old,GetTitle(doc,p),TextType(doc));
                doc.Regenerate();
                foreach(var sheet in p.SheetIds.Select(s=>doc.GetElement(s) as ViewSheet))CheckSheet(doc,sheet);
                foreach(var sheet in p.SheetIds.Select(s=>doc.GetElement(s) as ViewSheet)){sheet.Name=sheet.Name.Replace("_草稿","_核准施工");}
                Store(doc,p);if(tx.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("核准紀錄未提交。");
            }
        }
        internal static string Export(Document doc,string id,string directory)
        {
            var p=List(doc).Single(x=>x.Id==id);SleeveConstructionPolicy.Validate(p);
            VerifyDrawing(doc,p);
            if(p.Status=="作廢")throw new InvalidOperationException("此施工包已作廢，禁止重新匯出施工文件。");
            var impacts=Impacts(doc,p);
            if(impacts.Count>0)throw new InvalidOperationException("模型與此版定位資料不一致，請更新草稿或建立新版後匯出。\n"+string.Join("\n",impacts.Take(8)));
            var sheets=p.SheetIds.Select(s=>doc.GetElement(s) as ViewSheet??throw new InvalidOperationException("施工包圖紙已被刪除。")).ToList();
            foreach(var sheet in sheets)CheckSheet(doc,sheet);
            string folder=Path.Combine(directory,"HB-Sleeve-"+p.Id.Substring(0,8)+"-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,4));
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder,"查驗清單.csv"),SleeveConstructionPolicy.Csv(p),new UTF8Encoding(true));
            File.WriteAllText(Path.Combine(folder,"版次紀錄.json"),JsonConvert.SerializeObject(p,Formatting.Indented),new UTF8Encoding(true));
            try
            {
                if(!doc.Export(folder,sheets.Select(s=>s.Id).ToList(),new PDFExportOptions{Combine=true,FileName="套管定位與查驗_"+p.Status}))
                    throw new InvalidOperationException("Revit 未完成 PDF 輸出。");
            }
            catch(Exception ex){throw new InvalidOperationException("CSV 與版次紀錄已輸出，但 PDF 匯出失敗；此資料夾尚未完成。輸出位置："+folder+"\n"+ex.Message,ex);}
            File.WriteAllText(Path.Combine(folder,"README.txt"),"狀態："+p.Status+"\r\n"+p.Name+"／"+p.Revision+"\r\nCSV 為查驗資料交換表；本版提供 Revit 內登錄，尚未提供 CSV 自動回寫。\r\n定位尺寸依實體外包框中心，請確認族群實體與管心一致。需另核對現場基準、詳圖、標註重疊及人工核准文件。",new UTF8Encoding(true));
            return folder;
        }
        private static void VerifyDrawing(Document doc,SleeveConstructionPackage p)
        {
            var plan=doc.GetElement(p.PlanId) as ViewPlan;
            if(plan==null)throw new InvalidOperationException("施工定位圖已不存在。");
            foreach(var item in p.Items)
            {
                var note=doc.GetElement(item.NoteId) as TextNote;
                if(note==null||note.OwnerViewId!=plan.Id||Clean(note.Text)!=Clean(item.NoteText))
                    throw new InvalidOperationException("套管 "+item.Mark+" 的定位標註已修改或刪除，請先核對施工包。");
            }
            var header=doc.GetElement(p.HeaderId) as TextNote;
            if(header==null||Clean(header.Text)!=Clean(p.HeaderText))throw new InvalidOperationException("施工包標題／版次文字與紀錄不一致。");
            for(int page=0;page<p.TableIds.Count;page++)
            {
                var table=doc.GetElement(p.TableIds[page]) as ViewDrafting;
                if(table==null||!new FilteredElementCollector(doc,table.Id).OfClass(typeof(TextNote)).Cast<TextNote>().Any(n=>Clean(n.Text)==Clean(TableText(p,page))))
                    throw new InvalidOperationException("查驗表內容與施工包紀錄不一致。");
            }
        }
        internal static void Void(Document doc,string id,string reason)
        {
            if(string.IsNullOrWhiteSpace(reason))throw new InvalidOperationException("請在核准文件／簽核編號欄填寫作廢原因或文件編號。");
            var p=List(doc).Single(x=>x.Id==id);if(p.Status=="作廢")throw new InvalidOperationException("此版已作廢。");
            VerifyDrawing(doc,p);
            var old=JsonConvert.DeserializeObject<SleeveConstructionPackage>(JsonConvert.SerializeObject(p));
            p.Status="作廢";p.ApprovalReference+="；作廢："+reason.Trim();
            using(var tx=new Transaction(doc,"套管施工包作廢"))
            {
                tx.Start();UpdateHeader(doc,p,(ViewPlan)doc.GetElement(p.PlanId),TextType(doc));
                UpdateTables(doc,p,old,GetTitle(doc,p),TextType(doc));
                foreach(var sheet in p.SheetIds.Select(s=>doc.GetElement(s) as ViewSheet))sheet.Name+="_作廢";
                Store(doc,p);if(tx.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("作廢紀錄未提交。");
            }
        }
    }
}
