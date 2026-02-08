/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace ComposedDashboardWindow
{
    public partial class UISelectButton : GButton
    {
        public Controller button;
        public GGraph n0;
        public GGraph n1;
        public GGraph n2;
        public GTextField title;
        public const string URL = "ui://vbolh2kzsd1v5";

        public static UISelectButton CreateInstance()
        {
            return (UISelectButton)UIPackage.CreateObject("ComposedDashboardWindow", "SelectButton");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            button = GetController("button");
            n0 = (GGraph)GetChild("n0");
            n1 = (GGraph)GetChild("n1");
            n2 = (GGraph)GetChild("n2");
            title = (GTextField)GetChild("title");
        }
    }
}