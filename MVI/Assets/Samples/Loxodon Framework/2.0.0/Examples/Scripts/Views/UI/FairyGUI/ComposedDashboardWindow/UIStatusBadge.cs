/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace ComposedDashboardWindow
{
    public partial class UIStatusBadge : GComponent
    {
        public GTextField MessageText;
        public const string URL = "ui://vbolh2kzsd1v3";

        public static UIStatusBadge CreateInstance()
        {
            return (UIStatusBadge)UIPackage.CreateObject("ComposedDashboardWindow", "StatusBadge");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            MessageText = (GTextField)GetChild("MessageText");
        }
    }
}