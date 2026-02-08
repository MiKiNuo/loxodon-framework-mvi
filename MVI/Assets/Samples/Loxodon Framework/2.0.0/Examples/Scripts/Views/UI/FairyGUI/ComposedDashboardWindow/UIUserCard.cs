/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace ComposedDashboardWindow
{
    public partial class UIUserCard : GComponent
    {
        public GTextField UserNameText;
        public GTextField LevelText;
        public UISelectButton SelectButton;
        public GGroup info;
        public const string URL = "ui://vbolh2kzsd1v4";

        public static UIUserCard CreateInstance()
        {
            return (UIUserCard)UIPackage.CreateObject("ComposedDashboardWindow", "UserCard");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            UserNameText = (GTextField)GetChild("UserNameText");
            LevelText = (GTextField)GetChild("LevelText");
            SelectButton = (UISelectButton)GetChild("SelectButton");
            info = (GGroup)GetChild("info");
        }
    }
}