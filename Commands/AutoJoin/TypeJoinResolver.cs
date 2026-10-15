using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace YD_RevitTools.LicenseManager.Commands.AR.AutoJoin
{
    internal sealed class TypeJoinResolver
    {
        private readonly Document _doc;
        private readonly AutoJoinSettings _settings;
        private readonly TypeJoinPolicy _rules;
        private readonly Dictionary<ElementId, (string Family, string Type)> _types = new();

        internal TypeJoinResolver(Document doc, AutoJoinSettings settings)
        {
            _doc = doc;
            _settings = settings;
            if (settings.UseSameCategoryTypeRules)
            {
                if (settings.SameCategoryTypeRules == null || settings.SameCategoryTypeRules.Count == 0)
                    throw new InvalidOperationException("已啟用類型規則，請先新增至少一條規則。");
                _rules = new TypeJoinPolicy(settings.SameCategoryTypeRules);
            }
        }

        internal bool TryResolve(ClassifiedElement a, ClassifiedElement b, out Element winner)
        {
            winner = null;
            if (!_settings.AllowStructuralNonStructuralJoin && a.CategorySpec.IsStructural != b.CategorySpec.IsStructural) return false;
            if (a.CategorySpec.Key == b.CategorySpec.Key)
            {
                if (!_settings.UseSameCategoryTypeRules) return _settings.AllowSameCategoryJoin;
                var first = TypeOf(a.Element); var second = TypeOf(b.Element);
                int order = _rules.Resolve(a.CategorySpec.Key, first.Family, first.Type, second.Family, second.Type);
                if (order == 0) return false;
                winner = order < 0 ? a.Element : b.Element;
                return true;
            }
            int pa = _settings.PriorityKeys.IndexOf(a.CategorySpec.Key), pb = _settings.PriorityKeys.IndexOf(b.CategorySpec.Key);
            if (pa >= 0 && pb >= 0 && pa != pb) winner = pa < pb ? a.Element : b.Element;
            return true;
        }

        private (string Family, string Type) TypeOf(Element element)
        {
            ElementId id = element.GetTypeId();
            if (!_types.TryGetValue(id, out var value))
            {
                var type = _doc.GetElement(id) as ElementType;
                value = (type?.FamilyName ?? string.Empty, type?.Name ?? string.Empty);
                _types[id] = value;
            }
            return value;
        }

        internal static bool ApplyOrder(Document doc, Element a, Element b, Element winner)
        {
            if (winner == null) return false;
            Element loser = winner.Id.Equals(a.Id) ? b : a;
            if (JoinGeometryUtils.IsCuttingElementInJoin(doc, winner, loser)) return false;
            JoinGeometryUtils.SwitchJoinOrder(doc, winner, loser);
            if (!JoinGeometryUtils.IsCuttingElementInJoin(doc, winner, loser))
                throw new InvalidOperationException("接合切割順序未符合類型或類別優先規則。");
            return true;
        }
    }
}
