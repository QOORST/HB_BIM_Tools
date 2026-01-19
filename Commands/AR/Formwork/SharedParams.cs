using System.IO;
using System.Linq;
using Autodesk.Revit.DB;

namespace YD_RevitTools.LicenseManager.Commands.AR.Formwork
{
    public static class SharedParams
    {
        // 應用程式 ID，用於識別由本工具建立的 DirectShape
        public const string AppId = "YD_BIM_Tools";

        public const string GroupName = "YD_BIM_Tools";

        // 核心模板參數
        public const string P_Total = "模板_合計";           // 模板總面積
        public const string P_EffectiveArea = "模板_有效面積";  // 有效模板面積
        public const string P_Category = "模板_類別";         // 模板類別（牆/柱/梁/板）
        public const string P_HostId = "模板_主體ID";        // 宿主元素ID
        public const string P_AnalysisTime = "分析時間";
        public const string P_Thickness = "厚度";           // 厚度（mm）
        public const string P_Area = "面積";                // 面積（m²）
        public const string P_MaterialName = "材料名稱";     // 材料名稱

        // 參數 GUID（確保參數的唯一性和一致性）
        private static readonly System.Guid GUID_Total = new System.Guid("A1B2C3D4-E5F6-4A5B-8C9D-1E2F3A4B5C6D");
        private static readonly System.Guid GUID_EffectiveArea = new System.Guid("B2C3D4E5-F6A7-4B5C-9D1E-2F3A4B5C6D7E");
        private static readonly System.Guid GUID_Category = new System.Guid("C3D4E5F6-A7B8-4C5D-9E1F-3A4B5C6D7E8F");
        private static readonly System.Guid GUID_HostId = new System.Guid("D4E5F6A7-B8C9-4D5E-9F1A-4B5C6D7E8F9A");
        private static readonly System.Guid GUID_AnalysisTime = new System.Guid("E5F6A7B8-C9D1-4E5F-9A1B-5C6D7E8F9A0B");
        private static readonly System.Guid GUID_Thickness = new System.Guid("F6A7B8C9-D1E2-4F5A-9B1C-6D7E8F9A0B1C");
        private static readonly System.Guid GUID_Area = new System.Guid("A7B8C9D1-E2F3-4A5B-9C1D-7E8F9A0B1C2D");
        private static readonly System.Guid GUID_MaterialName = new System.Guid("B8C9D1E2-F3A4-4B5C-9D1E-8F9A0B1C2D3E");

        static bool _ensuredThisSession = false;

        public static void Ensure(Document doc)
        {
            if (_ensuredThisSession) return;

            var app = doc.Application;
            DefinitionFile defFile = null;
            string old = app.SharedParametersFilename;
            string temp = Path.Combine(Path.GetTempPath(), "YD_BIM_Tools_Params.txt");

            try
            {
                try { if (!string.IsNullOrEmpty(old) && File.Exists(old)) defFile = app.OpenSharedParameterFile(); }
                catch { defFile = null; }
                if (defFile == null)
                {
                    EnsureValidSharedParamFile(temp);
                    app.SharedParametersFilename = temp;
                    defFile = app.OpenSharedParameterFile();
                }
                if (defFile == null) throw new System.InvalidOperationException("無法開啟共用參數檔。");

                var group = defFile.Groups.Cast<DefinitionGroup>().FirstOrDefault(g => g.Name == GroupName)
                            ?? defFile.Groups.Create(GroupName);

                var needs = new (string, ForgeTypeId, System.Guid)[]
                {
                    (P_Total,         SpecTypeId.Area,        GUID_Total),         // 模板總面積（平方米）
                    (P_EffectiveArea, SpecTypeId.Area,        GUID_EffectiveArea), // 有效模板面積（平方米）
                    (P_Category,      SpecTypeId.String.Text, GUID_Category),      // 模板類別
                    (P_HostId,        SpecTypeId.String.Text, GUID_HostId),        // 宿主元素ID
                    (P_AnalysisTime,  SpecTypeId.String.Text, GUID_AnalysisTime),  // 分析時間
                    (P_Thickness,     SpecTypeId.Number,      GUID_Thickness),     // 厚度（mm）
                    (P_Area,          SpecTypeId.Area,        GUID_Area),          // 面積（m²）
                    (P_MaterialName,  SpecTypeId.String.Text, GUID_MaterialName)   // 材料名稱
                };

                void BindAll()
                {
                    var gmCat = doc.Settings.Categories.get_Item(BuiltInCategory.OST_GenericModel);
                    var catSet = app.Create.NewCategorySet(); catSet.Insert(gmCat);
                    var map = doc.ParameterBindings;

                    foreach (var tuple in needs)
                    {
                        string name = tuple.Item1;
                        ForgeTypeId spec = tuple.Item2;
                        System.Guid guid = tuple.Item3;

                        var def = FindDefinition(defFile, name);
                        if (def == null)
                        {
                            // 創建新的共用參數定義，包含 GUID
                            var options = new ExternalDefinitionCreationOptions(name, spec)
                            {
                                GUID = guid
                            };
                            def = group.Definitions.Create(options);
                        }

                        if (!map.Contains(def))
                        {
                            var ib = app.Create.NewInstanceBinding(catSet);
                            map.Insert(def, ib, GroupTypeId.Data);
                        }
                    }
                }

                if (doc.IsModifiable)
                {
                    using (var st = new SubTransaction(doc)) { st.Start(); BindAll(); st.Commit(); }
                }
                else
                {
                    using (var t = new Transaction(doc, "Ensure Shared Parameters")) { t.Start(); BindAll(); t.Commit(); }
                }

                _ensuredThisSession = true;
            }
            finally
            {
                app.SharedParametersFilename = old;
            }
        }

        private static Definition FindDefinition(DefinitionFile file, string name)
        {
            foreach (DefinitionGroup g in file.Groups)
                foreach (Definition d in g.Definitions)
                    if (d.Name == name) return d;
            return null;
        }

        private static void EnsureValidSharedParamFile(string path)
        {
            string header =
                "# This is a Revit shared parameter file." + System.Environment.NewLine +
                "# Do not edit manually." + System.Environment.NewLine +
                "*META VERSION 5 MINVERSION 1" + System.Environment.NewLine;
            if (!File.Exists(path)) { File.WriteAllText(path, header); return; }
            var lines = File.ReadAllLines(path);
            bool ok = lines.Length >= 3 && lines[0].StartsWith("# This is a Revit shared parameter file.")
                     && lines[1].StartsWith("#") && lines[2].StartsWith("*META");
            if (!ok) File.WriteAllText(path, header);
        }
    }
}
