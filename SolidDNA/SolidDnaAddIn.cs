using System.Runtime.InteropServices;
using CADBooster.SolidDna;

namespace SolidDNA
{
    [Guid("2E1D5140-7E1A-4A4F-8FBC-2F0F2C92C751")]
    [ComVisible(true)]
    public class SolidDnaAddIn : SolidAddIn
    {
        public static SolidDnaAddIn Instance { get; private set; }

        public override void PreConnectToSolidWorks()
        {
        }

        public override void PreLoadPlugIns()
        {
            PlugInIntegration.AddPlugInToLoad<SolidDnaPlugin>();
        }

        public override void ApplicationStartup()
        {
            Instance = this;
        }
    }
}