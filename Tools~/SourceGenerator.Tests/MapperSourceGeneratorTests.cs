#nullable disable
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SourceGenerator;

namespace SourceGenerator.Tests
{
    [NonParallelizable]
    public sealed class MapperSourceGeneratorTests
    {
        private static readonly MetadataReference[] PlatformReferences =
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
            .Split(Path.PathSeparator)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToArray();

        private const string RuntimeStubs = @"
namespace MVI { public abstract class MviViewModel { } }
namespace UnityEngine
{
    public enum RuntimeInitializeLoadType { AfterAssembliesLoaded = 2 }
    [System.AttributeUsage(System.AttributeTargets.Method)]
    public sealed class RuntimeInitializeOnLoadMethodAttribute : System.Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType loadType) { }
    }
}
namespace UnityEditor
{
    [System.AttributeUsage(System.AttributeTargets.Method)]
    public sealed class InitializeOnLoadMethodAttribute : System.Attribute { }
}";

        private const string CounterConsumer = @"
public sealed class State : MVI.IState
{
    public int Value { get; set; }
    public bool IsUpdateNewState { get; set; }
}
public sealed class ViewModel : MVI.MviViewModel
{
    private int value;
    [MVI.MviIgnore] public int Writes { get; private set; }
    public int Value { get => value; set { this.value = value; Writes++; } }
}";

        private MetadataReference runtimeReference;
        private Assembly runtimeAssembly;

        [OneTimeSetUp]
        public void CompileRuntime()
        {
            var options = new CSharpParseOptions(LanguageVersion.CSharp9);
            var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures");
            var trees = new[] { "IState.cs", "MviMappingAttributes.cs", "MviStateMapper.cs" }
                .Select(name => CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(fixturePath, name)), options))
                .Append(CSharpSyntaxTree.ParseText(RuntimeStubs, options));
            var compilation = CSharpCompilation.Create("MviMappingRuntime_" + Guid.NewGuid().ToString("N"),
                trees, PlatformReferences, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var image = Emit(compilation);
            runtimeReference = MetadataReference.CreateFromImage(image);
            runtimeAssembly = AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(image));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Initialization_ShouldRegisterACSharp9GeneratedConsumer(bool editor)
        {
            var consumer = CompileConsumer(CounterConsumer, editor);
            var initializer = Initializer(consumer.Assembly);
            var attributeNames = initializer.GetCustomAttributesData().Select(attribute => attribute.AttributeType.FullName).ToArray();
            CollectionAssert.Contains(attributeNames, "UnityEngine.RuntimeInitializeOnLoadMethodAttribute");
            Assert.AreEqual(editor, attributeNames.Contains("UnityEditor.InitializeOnLoadMethodAttribute"));
            var runtimeAttribute = initializer.GetCustomAttributesData().Single(attribute => attribute.AttributeType.FullName == "UnityEngine.RuntimeInitializeOnLoadMethodAttribute");
            Assert.AreEqual(2, runtimeAttribute.ConstructorArguments[0].Value);

            var state = Activator.CreateInstance(consumer.Assembly.GetType("State"));
            var viewModel = Activator.CreateInstance(consumer.Assembly.GetType("ViewModel"));
            Set(state, "Value", 7);
            Assert.IsFalse(TryMap(state, viewModel));

            initializer.Invoke(null, null);

            Assert.IsTrue(TryMap(state, viewModel));
            Assert.AreEqual(7, Get(viewModel, "Value"));
            Assert.AreEqual(1, Get(viewModel, "Writes"));
            Assert.IsTrue(TryMap(state, viewModel));
            Assert.AreEqual(1, Get(viewModel, "Writes"));

            Set(state, "IsUpdateNewState", true);
            Assert.IsTrue(TryMap(state, viewModel));
            Assert.AreEqual(2, Get(viewModel, "Writes"));
        }

        [Test]
        public void SameNamedMappersInDifferentAssemblies_ShouldBothRemainRegistered()
        {
            var first = CompileConsumer(CounterConsumer);
            var second = CompileConsumer(CounterConsumer);
            Assert.AreEqual(Initializer(first.Assembly).DeclaringType.FullName, Initializer(second.Assembly).DeclaringType.FullName);
            Initializer(first.Assembly).Invoke(null, null);
            Initializer(second.Assembly).Invoke(null, null);

            foreach (var consumer in new[] { first, second })
            {
                var state = Activator.CreateInstance(consumer.Assembly.GetType("State"));
                var viewModel = Activator.CreateInstance(consumer.Assembly.GetType("ViewModel"));
                Set(state, "Value", 11);
                Assert.IsTrue(TryMap(state, viewModel));
                Assert.AreEqual(11, Get(viewModel, "Value"));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AliasesAndIgnore_ShouldWorkFromEitherSide(bool aliasOnViewModel)
        {
            var stateAlias = aliasOnViewModel ? "" : "[MVI.MviMap(\"UserName\")]";
            var viewModelAlias = aliasOnViewModel ? "[MVI.MviMap(\"Name\")]" : "";
            var consumer = CompileConsumer(@"
public sealed class State : MVI.IState
{
    " + stateAlias + @" public string Name { get; set; }
    [MVI.MviIgnore] public string Hidden { get; set; }
    public bool IsUpdateNewState { get; set; }
}
public sealed class ViewModel : MVI.MviViewModel
{
    " + viewModelAlias + @" public string UserName { get; set; }
    public string Hidden { get; set; }
}");
            Initializer(consumer.Assembly).Invoke(null, null);
            var state = Activator.CreateInstance(consumer.Assembly.GetType("State"));
            var viewModel = Activator.CreateInstance(consumer.Assembly.GetType("ViewModel"));
            Set(state, "Name", "Alice");
            Set(state, "Hidden", "secret");

            Assert.IsTrue(TryMap(state, viewModel));
            Assert.AreEqual("Alice", Get(viewModel, "UserName"));
            Assert.IsNull(Get(viewModel, "Hidden"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CaseConflicts_ShouldPreserveDerivedFirstMappingAndDiagnosticOrder(bool diagnostics)
        {
            var consumer = CompileConsumer(@"
public class BaseState : MVI.IState
{
    public int Value { get; set; }
    public bool IsUpdateNewState { get; set; }
}
public sealed class State : BaseState { public int value { get; set; } }
public class BaseViewModel : MVI.MviViewModel { public int Value { get; set; } }
public sealed class ViewModel : BaseViewModel { public int value { get; set; } }
", diagnostics: diagnostics);
            var conflicts = consumer.Diagnostics.Where(diagnostic => diagnostic.Id == "MVI001").ToArray();
            Assert.AreEqual(diagnostics ? 2 : 0, conflicts.Length);
            if (diagnostics)
            {
                StringAssert.Contains("'ViewModel'", conflicts[0].GetMessage());
                StringAssert.Contains("'State'", conflicts[1].GetMessage());
            }

            Initializer(consumer.Assembly).Invoke(null, null);
            var state = Activator.CreateInstance(consumer.Assembly.GetType("State"));
            var viewModel = Activator.CreateInstance(consumer.Assembly.GetType("ViewModel"));
            Set(state, "Value", 3);
            Set(state, "value", 9);
            Assert.IsTrue(TryMap(state, viewModel));
            Assert.AreEqual(9, Get(viewModel, "value"));
            Assert.AreEqual(0, Get(viewModel, "Value"));
        }

        private (Assembly Assembly, Diagnostic[] Diagnostics) CompileConsumer(string source, bool editor = false, bool diagnostics = false)
        {
            var symbols = new[] { editor ? "UNITY_EDITOR" : null, diagnostics ? "MVI_GENERATOR_DIAGNOSTICS" : null }.Where(symbol => symbol != null);
            var options = new CSharpParseOptions(LanguageVersion.CSharp9, preprocessorSymbols: symbols);
            var compilation = CSharpCompilation.Create("MviMappingConsumer_" + Guid.NewGuid().ToString("N"),
                new[] { CSharpSyntaxTree.ParseText(source, options) }, PlatformReferences.Append(runtimeReference),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { new MapperSourceGenerator() }, parseOptions: options);
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);
            Assert.IsNull(driver.GetRunResult().Results.Single().Exception);
            Assert.AreEqual(1, driver.GetRunResult().GeneratedTrees.Length);
            var image = Emit(output);
            return (AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(image)), generatorDiagnostics.ToArray());
        }

        private static byte[] Emit(Compilation compilation)
        {
            using var image = new MemoryStream();
            var result = compilation.Emit(image);
            Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)));
            return image.ToArray();
        }

        private static MethodInfo Initializer(Assembly assembly) => assembly.GetType("MVI.Generated.GeneratedStateMapper")
            .GetMethod("RegisterMapper", BindingFlags.Static | BindingFlags.NonPublic);

        private bool TryMap(object state, object viewModel) => (bool)runtimeAssembly.GetType("MVI.MviStateMapper")
            .GetMethod("TryMap").Invoke(null, new[] { state, viewModel });

        private static object Get(object target, string name) => target.GetType().GetProperty(name).GetValue(target);
        private static void Set(object target, string name, object value) => target.GetType().GetProperty(name).SetValue(target, value);
    }
}
