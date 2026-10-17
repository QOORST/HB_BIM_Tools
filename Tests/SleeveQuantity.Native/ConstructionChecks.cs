using System;
using System.Collections;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;

namespace HB.SleeveQuantity.Native
{
    public sealed partial class Startup
    {
        private void RunConstructionChecks(FamilyInstance sleeve,Level level)
        {
            ViewPlan source=null;Grid a=null,b=null;TextNote manual=null;
            Tx("QA construction package source",()=>
            {
                Call("SleeveQuantityData","EnsureParameters",_doc);_doc.Regenerate();
                sleeve.get_Parameter(Param(2)).Set("CONSTRUCTION-QA");
                Call("SleeveStableNumbering","Assign",_doc,new[]{sleeve},null);
                var type=new FilteredElementCollector(_doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().First(t=>t.ViewFamily==ViewFamily.FloorPlan);
                source=ViewPlan.Create(_doc,type.Id,level.Id);source.Name="QA Construction Source";source.Scale=50;
                source.CropBox=new BoundingBoxXYZ{Min=new XYZ(-10,-10,-1),Max=new XYZ(10,10,10)};source.CropBoxActive=false;
                a=Grid.Create(_doc,Line.CreateBound(new XYZ(-2,-10,0),new XYZ(-2,10,0)));a.Name="QA-A";
                b=Grid.Create(_doc,Line.CreateBound(new XYZ(-10,-2,0),new XYZ(10,-2,0)));b.Name="QA-B";
                manual=TextNote.Create(_doc,source.Id,new XYZ(-4,-4,0),"QA MANUAL NOTE",new TextNoteOptions(_doc.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType)));
            });
            var title=new FilteredElementCollector(_doc,_sheet).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>().First(f=>f.Category.Id.Value==(long)BuiltInCategory.OST_TitleBlocks).Symbol;
            object request=Activator.CreateInstance(T("SleeveConstructionRequest"),true);
            void Set(string key,object value)=>request.GetType().GetField(key,Flags).SetValue(request,value);
            Set("Name","QA-Construction");Set("Revision","R01");Set("Zone","CONSTRUCTION-QA");Set("SourceViewId",source.Id);Set("ScopeLevelId",level.Id);Set("GridAId",a.Id);Set("GridBId",b.Id);Set("TitleBlockId",title.Id);
            var preview=Call("SleeveConstructionService","Preview",_doc,request);
            Check(Items(Property(preview,"Items")).Count==1,"Construction package preview uses exact level and zone membership");
            var package=Call("SleeveConstructionService","CreateOrUpdate",_doc,request);
            string id=(string)Property(package,"Id"),planId=(string)Property(package,"PlanId");
            var plan=(ViewPlan)_doc.GetElement(planId);var row=Items(Property(package,"Items")).Single();
            string noteId=(string)Property(row,"NoteId");
            Check(Items(Property(package,"SheetIds")).Count==2&&Items(Property(package,"TableIds")).Count==1,"Construction package creates native plan and field quantity table sheets");
            Check(manual.IsValidObject&&new FilteredElementCollector(_doc,plan.Id).OfClass(typeof(TextNote)).Cast<TextNote>().Any(n=>n.Text.Contains("QA MANUAL NOTE")),"Original and copied manual annotations are preserved");
            XYZ manualPosition=null;
            Tx("QA manual annotation positioning",()=>{var note=(TextNote)_doc.GetElement(noteId);ElementTransformUtils.MoveElement(_doc,note.Id,new XYZ(.3,.2,0));_doc.Regenerate();manualPosition=note.Coord;});
            var updated=Call("SleeveConstructionService","CreateOrUpdate",_doc,request);
            Check((string)Property(updated,"Id")==id&&(string)Property(updated,"PlanId")==planId&&((TextNote)_doc.GetElement(noteId)).Coord.DistanceTo(manualPosition)<1e-8,
                "Draft rerun preserves package, plan, annotation identity and manual positioning");
            Check(Items(Call("SleeveConstructionService","Impacts",_doc,updated)).Count==0,"Unchanged model has no construction package impacts");
            foreach(string sheetId in Items(Property(package,"SheetIds")))
                foreach(var viewport in new FilteredElementCollector(_doc,_doc.GetElement(sheetId).Id).OfClass(typeof(Viewport)).Cast<Viewport>())
                {
                    var view=(View)_doc.GetElement(viewport.ViewId);Log("Construction view "+view.Name+" scale="+view.Scale+" template="+view.ViewTemplateId+" box="+viewport.GetBoxOutline().MinimumPoint+" / "+viewport.GetBoxOutline().MaximumPoint);
                    foreach(var note in new FilteredElementCollector(_doc,view.Id).OfClass(typeof(TextNote)).Cast<TextNote>())Log("Note "+note.Id+" pos="+note.Coord+" width="+note.Width+" hidden="+note.IsHidden(view)+" bbox="+note.get_BoundingBox(view)?.Min+" / "+note.get_BoundingBox(view)?.Max);
                }
            using(var form=(System.Windows.Forms.Form)Activator.CreateInstance(T("SleeveConstructionForm"),Flags,null,new object[]{_doc},null))
            {
                form.Show();form.Refresh();
                using(var bitmap=new System.Drawing.Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new System.Drawing.Rectangle(0,0,form.Width,form.Height));bitmap.Save(Path.Combine(_output,"construction-ui-create.png"));}
                var tabs=form.Controls.Cast<System.Windows.Forms.Control>().SelectMany(c=>c.Controls.Cast<System.Windows.Forms.Control>()).OfType<System.Windows.Forms.TabControl>().Single();
                tabs.SelectedIndex=1;form.Refresh();
                using(var bitmap=new System.Drawing.Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new System.Drawing.Rectangle(0,0,form.Width,form.Height));bitmap.Save(Path.Combine(_output,"construction-ui-field.png"));}
                var edit=(System.Windows.Forms.DataGridView)form.GetType().GetField("_field",Flags).GetValue(form);edit.Rows[0].Cells[9].Value="QA unsaved note";
                bool unsavedRejected=false;try{form.GetType().GetMethod("RequireSavedField",Flags).Invoke(form,null);}catch{unsavedRejected=true;}
                Check(unsavedRejected,"Unsaved field edits block actions that could omit them");
                form.GetType().GetMethod("SaveField",Flags).Invoke(form,null);
                Check(!(bool)form.GetType().GetField("_fieldDirty",Flags).GetValue(form),"Saving field edits clears pending state");
                form.Close();
            }
            string folder=(string)Call("SleeveConstructionService","Export",_doc,id,_output);
            Check(Directory.GetFiles(folder,"*.pdf").Length==1&&File.Exists(Path.Combine(folder,"查驗清單.csv")),"Native construction package PDF and inspection CSV exported");
            // Editing managed annotation content is protected even though moving it is allowed.
            Tx("QA alter managed annotation text",()=>((TextNote)_doc.GetElement(noteId)).Text="QA USER EDIT");
            bool rejected=false;try{Call("SleeveConstructionService","CreateOrUpdate",_doc,request);}catch{rejected=true;}
            Check(rejected&&((TextNote)_doc.GetElement(noteId)).Text.Contains("QA USER EDIT"),"Draft rerun rejects conflicting human annotation edits without overwriting them");
            rejected=false;try{Call("SleeveConstructionService","Export",_doc,id,_output);}catch{rejected=true;}
            Check(rejected,"Export rejects manually altered managed annotation content");
            Tx("QA restore managed annotation",()=>((TextNote)_doc.GetElement(noteId)).Text=(string)Property(row,"NoteText"));
            // Out-of-sheet placement must be rejected without leaving a package or new views behind.
            int viewsBefore=new FilteredElementCollector(_doc).OfClass(typeof(View)).GetElementCount();
            Tx("QA overflow source",()=>source.Scale=1);
            Set("Revision","OVERFLOW");rejected=false;
            try{Call("SleeveConstructionService","CreateOrUpdate",_doc,request);}catch{rejected=true;}
            Check(rejected&&new FilteredElementCollector(_doc).OfClass(typeof(View)).GetElementCount()==viewsBefore&&Items(Call("SleeveConstructionService","List",_doc)).Count==1,
                "Overflow rolls back all new package sheets, views and metadata");
            Tx("QA restore source scale",()=>source.Scale=50);Set("Revision","R01");
            Call("SleeveConstructionService","Approve",_doc,id,"QA Engineer","QA-APPROVAL-001");
            var approved=Items(Call("SleeveConstructionService","List",_doc)).Single();
            Check((string)Property(approved,"Status")=="核准施工"&&(string)Property(approved,"ApprovalReference")=="QA-APPROVAL-001","Human approval and external reference persist in the model");
            rejected=false;try{Call("SleeveConstructionService","CreateOrUpdate",_doc,request);}catch{rejected=true;}
            Check(rejected,"Approved construction revision cannot be overwritten");
            var record=Activator.CreateInstance(T("SleeveFieldRecord"));
            record.GetType().GetProperty("ElementId").SetValue(record,sleeve.UniqueId);
            record.GetType().GetProperty("Installation").SetValue(record,"已安裝");
            var records=(IList)Activator.CreateInstance(typeof(System.Collections.Generic.List<>).MakeGenericType(T("SleeveFieldRecord")));records.Add(record);
            var expected=(DateTime)Property(approved,"UpdatedUtc");
            Call("SleeveConstructionService","SaveField",_doc,id,records,expected);
            rejected=false;try{Call("SleeveConstructionService","SaveField",_doc,id,records,expected);}catch{rejected=true;}
            Check(rejected,"Stale field edit cannot overwrite newer inspection records");
            var fieldPackage=Items(Call("SleeveConstructionService","List",_doc)).Single();
            using(var movement=new TransactionGroup(_doc,"QA installed sleeve impact"))
            {
                movement.Start();Tx("QA shift installed sleeve",()=>ElementTransformUtils.MoveElement(_doc,sleeve.Id,new XYZ(.1,0,0)));
                var impacts=Items(Call("SleeveConstructionService","Impacts",_doc,fieldPackage));
                Check(impacts.Any(x=>x.ToString().Contains("已安裝，須現場確認")),"Moved installed sleeve is flagged for field confirmation");
                rejected=false;try{Call("SleeveConstructionService","Export",_doc,id,_output);}catch{rejected=true;}
                Check(rejected,"Export blocks outdated approved package after model movement");movement.RollBack();
            }
            Call("SleeveConstructionService","Void",_doc,id,"QA superseded");
            rejected=false;try{Call("SleeveConstructionService","Export",_doc,id,_output);}catch{rejected=true;}
            Check(rejected&&(string)Property(Items(Call("SleeveConstructionService","List",_doc)).Single(),"Status")=="作廢","Voided revision retains model records and blocks export");
            using(var pagination=new TransactionGroup(_doc,"QA construction pagination"))
            {
                pagination.Start();
                Tx("QA thirteen sleeve table",()=>
                {
                    for(int i=1;i<=12;i++)
                    {
                        var copy=_doc.GetElement(ElementTransformUtils.CopyElement(_doc,sleeve.Id,new XYZ(i*.15,0,0)).Single());
                        copy.get_Parameter(BuiltInParameter.ALL_MODEL_MARK).Set("QA-"+i.ToString("D3"));
                        var levelParameter=copy.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM)??copy.get_Parameter(BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM);
                        if(levelParameter==null||levelParameter.AsElementId()!=level.Id&&(levelParameter.IsReadOnly||!levelParameter.Set(level.Id)))throw new InvalidOperationException("Cannot set pagination fixture level");
                        copy.get_Parameter(Param(1)).Set("納入");copy.get_Parameter(Param(2)).Set("CONSTRUCTION-QA");
                        Log("Pagination copy "+copy.Id+" level="+((Level)Call("SleeveGlLevel","Actual",_doc,copy)).Name+" expected="+level.Name+" zone="+copy.get_Parameter(Param(2)).AsString());
                    }
                });
                foreach(var q in Items(Call("SleeveQuantityData","Read",_doc,null,null)))Log("Pagination row "+Property(q,"Mark")+" level="+Property(q,"Level")+" zone="+Property(q,"Zone")+" included="+q.GetType().GetField("Included",Flags).GetValue(q));
                Set("Revision","R02");var paged=Call("SleeveConstructionService","CreateOrUpdate",_doc,request);
                Log("Pagination counts: items="+Items(Property(paged,"Items")).Count+" tables="+Items(Property(paged,"TableIds")).Count+" sheets="+Items(Property(paged,"SheetIds")).Count);
                Check(Items(Property(paged,"Items")).Count==13&&Items(Property(paged,"TableIds")).Count==2&&Items(Property(paged,"SheetIds")).Count==3,
                    "Thirteen sleeves paginate into two field tables and three native sheets");
                Call("SleeveConstructionService","Export",_doc,(string)Property(paged,"Id"),_output);
                pagination.RollBack();
            }
        }
    }
}
