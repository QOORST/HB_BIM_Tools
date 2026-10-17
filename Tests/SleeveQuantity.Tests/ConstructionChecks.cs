using System;
using System.Collections.Generic;
using YD_RevitTools.LicenseManager.Commands.MEP;

internal static class ConstructionChecks
{
    internal static void Run(Action<bool,string> check)
    {
        SleeveConstructionPackage New()=>new SleeveConstructionPackage{Name="A棟2F",Revision="R01",GridFrame=new double[12],
            Items=new List<SleeveConstructionItem>{new SleeveConstructionItem{Id="a",Mark="PS-001",Check="正常",OffsetA=100,OffsetB=-200,Elevation=3000}}};
        bool Reject(Action action){try{action();return false;}catch(InvalidOperationException){return true;}}
        var p=New();SleeveConstructionPolicy.Validate(p);check(p.Status=="草稿","Construction package starts as draft");
        check(Reject(()=>SleeveConstructionPolicy.Approve(p,"","")),"Approval requires human and source reference");
        p.Items[0].Check="待確認";check(Reject(()=>SleeveConstructionPolicy.Approve(p,"王工","簽核-01")),"Unresolved sleeve cannot be approved");
        p.Items[0].Check="正常";SleeveConstructionPolicy.Approve(p,"王工","簽核-01");check(p.Status=="核准施工"&&p.ApprovedUtc.HasValue,"Human approval is recorded separately from model check");
        check(Reject(()=>SleeveConstructionPolicy.Approve(p,"王工","簽核-02")),"Approved revision cannot be approved again as draft");
        p=New();p.Items.Add(new SleeveConstructionItem{Id="b",Mark="ps-1"});check(Reject(()=>SleeveConstructionPolicy.Validate(p)),"Canonical duplicate sleeve marks block construction package");
        var before=New().Items[0];var after=New().Items[0];after.X=1;check(SleeveConstructionPolicy.Changed(before,after).Contains("移位"),"One millimetre site change is detected");
        after=New().Items[0];after.OffsetA=102;check(SleeveConstructionPolicy.Changed(before,after).Contains("定位基準"),"Changed grid datum is not hidden as unchanged position");
        before.Orientation=new[]{1.0,0,0,0,1,0,0,0,1};after=New().Items[0];after.Orientation=new[]{0.0,1,0,-1,0,0,0,0,1};
        check(SleeveConstructionPolicy.Changed(before,after).Contains("方向變更"),"Rotation without center movement is detected");
        before.Orientation=new[]{1.0,0,0,0,1,0,0,0,1};after=New().Items[0];after.Orientation=new[]{0.0,1,0,-1,0,0,0,0,1};
        check(SleeveConstructionPolicy.Changed(before,after).Contains("方向變更"),"Rotation without center movement is detected");
        var f=new SleeveFieldRecord{ElementId="a",Inspection="合格"};check(Reject(()=>SleeveConstructionPolicy.ValidateField(f)),"Uninstalled or unmeasured inspection cannot be marked passed");
        f.Installation="已安裝";f.Person="王工";f.Date="2026-10-05";f.MeasuredA=102;f.MeasuredB=-202;f.MeasuredElevation=3003;
        SleeveConstructionPolicy.ValidateField(f);check(true,"Installed and measured human inspection is accepted");
        f.MeasuredA=double.NaN;check(Reject(()=>SleeveConstructionPolicy.ValidateField(f)),"Invalid measurement is rejected");f.MeasuredA=102;
        p=New();p.FieldRecords.Add(f);string csv=SleeveConstructionPolicy.Csv(p);check(csv.Contains(",102,-202,3003,2,-2,3,"),"Field CSV reports signed millimetre deviations");
        check(SleeveConstructionPolicy.Cell(" =cmd").Contains("' =cmd"),"Spreadsheet formula text is escaped");
    }
}
