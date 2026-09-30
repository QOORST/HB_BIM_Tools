using System.IO;
using System.Reflection;
namespace HB.CadRevision
{
    internal static class ReportNavigation
    {
        internal static string Html()
        {
            using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("HB.CadRevision.svg-navigation.js"))
            using(var reader=new StreamReader(stream))return "<script data-hb-navigation>"+reader.ReadToEnd()+"</script>";
        }
    }
}
