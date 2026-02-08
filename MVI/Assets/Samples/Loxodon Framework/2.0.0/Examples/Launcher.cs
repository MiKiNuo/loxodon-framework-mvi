/*
 * MIT License
 *
 * Copyright (c) 2018 Clark Yang
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy of
 * this software and associated documentation files (the "Software"), to deal in
 * the Software without restriction, including without limitation the rights to
 * use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies
 * of the Software, and to permit persons to whom the Software is furnished to do so,
 * subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 */

using UnityEngine;
using System;
using System.Globalization;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Loxodon.Framework.Contexts;
using Loxodon.Framework.Views;
using Loxodon.Framework.Binding;
using Loxodon.Framework.Localizations;
using Loxodon.Framework.Services;
using FairyGUI;
using ComposedDashboardWindow;
using MVI.Examples.FairyGUI.Composed.Views;
using MVI.FairyGUI;

namespace Loxodon.Framework.Examples
{
    public class Launcher : MonoBehaviour
    {
        // UI 演示类型（供用户在 Inspector 中切换）。
        public enum DemoUiKind
        {
            UGUI = 0,
            FairyGUI = 1
        }

        [Header("Demo 切换")] [SerializeField] private DemoUiKind demoUiKind = DemoUiKind.UGUI;
        [Header("FairyGUI 运行时包路径")] [SerializeField] private string[] runtimeFairyPackagePaths = { "Res/ComposedDashboardWindow", "ComposedDashboardWindow" };

        // 运行时可注入自定义 Fairy 包加载器（例如 YooAsset）。
        public static IFairyPackageLoader RuntimeFairyPackageLoader { get; set; }

        private ApplicationContext context;
        private Coroutine demoCoroutine;
        private WindowContainer windowContainer;

        void Awake()
        {
            GlobalWindowManagerBase windowManager = FindObjectOfType<GlobalWindowManagerBase>();
            if (windowManager == null)
                throw new NotFoundException("Not found the GlobalWindowManager.");

            context = Context.GetApplicationContext();

            IServiceContainer container = context.GetContainer();

            /* Initialize the data binding service */
            BindingServiceBundle bundle = new BindingServiceBundle(context.GetContainer());
            bundle.Start();
            // FairyGUI 绑定扩展（让 Loxodon 能识别 GButton.onClick 等 EventListener）。
            FairyGUIBindingServiceBundle fairyBundle = new FairyGUIBindingServiceBundle(context.GetContainer());
            fairyBundle.Start();

            /* Initialize the ui view locator and register UIViewLocator */
            container.Register<IUIViewLocator>(new ResourcesViewLocator());

            /* Initialize the localization service */
            //CultureInfo cultureInfo = Locale.GetCultureInfoByLanguage (SystemLanguage.English);
            CultureInfo cultureInfo = Locale.GetCultureInfo();
            var localization = Localization.Current;
            localization.CultureInfo = cultureInfo;
            localization.AddDataProvider(new ResourcesDataProvider("LocalizationExamples", new XmlDocumentParser()));

            /* register Localization */
            container.Register<Localization>(localization);

            /* register AccountRepository */
            IAccountRepository accountRepository = new AccountRepository();
            container.Register<IAccountService>(new AccountService(accountRepository));

            /* Enable window state broadcast */
            GlobalSetting.enableWindowStateBroadcast = true;
            /*
             * Use the CanvasGroup.blocksRaycasts instead of the CanvasGroup.interactable
             * to control the interactivity of the view
             */
            GlobalSetting.useBlocksRaycastsInsteadOfInteractable = true;
        }

        IEnumerator Start()
        {
            // 统一入口：根据配置启动 Demo。
            if (demoUiKind == DemoUiKind.FairyGUI)
            {
                // FairyGUI Demo：先加载包、注册扩展，再创建视图。
                yield return LoadFairyGuiPackages();
                var go = new GameObject("FairyGUI-ComposedDashboard");
                go.AddComponent<FairyComposedDashboardView>();
                yield return null;
            }
            else
            {
                IUIViewLocator locator = context.GetService<IUIViewLocator>();
                WindowContainer winContainer = WindowContainer.Create("MAIN");
                // 组合式 Demo（UGUI + Loxodon）
                //Loxodon.Framework.Examples.Composed.Views.ComposedDashboardWindow window =locator.LoadWindow<Loxodon.Framework.Examples.Composed.Views.ComposedDashboardWindow>(winContainer,"UI/Composed/ComposedDashboard");
                // 原登录/注册 Demo
                StartupWindow window = locator.LoadWindow<StartupWindow>(winContainer, "UI/Startup/Startup");
                window.Create();
                ITransition transition = window.Show().OnStateChanged((w, state) =>
                {
                    //log.DebugFormat("Window:{0} State{1}",w.Name,state);
                });
                yield return transition.WaitForDone();
            }

        }

        // FairyGUI 包加载：默认支持 Editor 与 Resources 路径，外部可注入自定义加载器。
        private IEnumerator LoadFairyGuiPackages()
        {
            var loader = RuntimeFairyPackageLoader ?? new LauncherFairyPackageLoader();
            var task = loader.LoadAsync(BuildFairyPackagePaths(), CancellationToken.None).AsTask();
            while (!task.IsCompleted)
            {
                yield return null;
            }

            if (task.IsFaulted)
            {
                Debug.LogException(task.Exception);
                yield break;
            }

            if (task.IsCanceled)
            {
                yield break;
            }

            // 注册自定义组件扩展（可在包加载前后调用，确保创建 UI 前已注册）。
            ComposedDashboardWindowBinder.BindAll();
        }

        private IReadOnlyList<string> BuildFairyPackagePaths()
        {
#if UNITY_EDITOR
            return new[] { "Assets/Res/ComposedDashboardWindow" };
#else
            if (runtimeFairyPackagePaths != null && runtimeFairyPackagePaths.Length > 0)
            {
                return runtimeFairyPackagePaths;
            }

            return new[] { "Res/ComposedDashboardWindow", "ComposedDashboardWindow" };
#endif
        }

        private sealed class LauncherFairyPackageLoader : IFairyPackageLoader
        {
            public ValueTask LoadAsync(IReadOnlyList<string> packagePaths, CancellationToken cancellationToken = default)
            {
                if (packagePaths == null || packagePaths.Count == 0)
                {
                    return default;
                }

                var loadedAny = false;
                Exception lastException = null;
                for (var i = 0; i < packagePaths.Count; i++)
                {
                    var path = packagePaths[i];
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        continue;
                    }

                    try
                    {
                        UIPackage.AddPackage(path);
                        loadedAny = true;
                    }
                    catch (Exception ex)
                    {
                        lastException = ex;
                        Debug.LogWarning($"FairyGUI package load failed: {path}");
                    }
                }

                if (!loadedAny && lastException != null)
                {
                    throw new InvalidOperationException("No FairyGUI package could be loaded.", lastException);
                }

                return default;
            }
        }
    }
}
