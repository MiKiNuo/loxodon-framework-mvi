/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace ComposedDashboardWindow
{
    public partial class UICounterCard : GComponent
    {
        public GTextField LabelText;
        public UIIncrementButton IncrementButton;
        public GTextField CountText;
        public GGroup title;
        public const string URL = "ui://vbolh2kzsd1v1";

        public static UICounterCard CreateInstance()
        {
            return (UICounterCard)UIPackage.CreateObject("ComposedDashboardWindow", "CounterCard");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            LabelText = (GTextField)GetChild("LabelText");
            IncrementButton = (UIIncrementButton)GetChild("IncrementButton");
            CountText = (GTextField)GetChild("CountText");
            title = (GGroup)GetChild("title");
        }
    }
}