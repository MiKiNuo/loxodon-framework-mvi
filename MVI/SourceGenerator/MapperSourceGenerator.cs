using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace SourceGenerator
{
    /// <summary>
    /// 生成 IState -> MviViewModel 的强类型映射，避免运行期反射。
    /// </summary>
    [Generator]
    public class MapperSourceGenerator : ISourceGenerator
    {
        private const string DiagnosticsSymbol = "MVI_GENERATOR_DIAGNOSTICS";
        private static readonly StringComparer PropertyNameComparer = StringComparer.OrdinalIgnoreCase;

        private static readonly DiagnosticDescriptor CaseInsensitiveConflictDescriptor = new(
            "MVI001",
            "大小写不敏感的属性名冲突",
            "类型 '{0}' 存在仅大小写不同的公共属性，映射使用 '{1}' 并跳过 '{2}'。",
            "MVI.Generator",
            DiagnosticSeverity.Info,
            true);

        public void Initialize(GeneratorInitializationContext context)
        {
            context.RegisterForSyntaxNotifications(() => new CandidateReceiver());
        }

        public void Execute(GeneratorExecutionContext context)
        {
            if (context.SyntaxReceiver is not CandidateReceiver receiver)
                return;

            var compilation = context.Compilation;
            var stateInterface = compilation.GetTypeByMetadataName("MVI.IState");
            var viewModelBase = compilation.GetTypeByMetadataName("MVI.MviViewModel");

            if (stateInterface is null || viewModelBase is null)
            {
                return;
            }

            var stateTypes = CollectTypes(compilation, receiver.TypeCandidates,
                symbol => ImplementsInterface(symbol, stateInterface));

            var viewModelTypes = CollectTypes(compilation, receiver.TypeCandidates,
                symbol => InheritsFrom(symbol, viewModelBase));

            if (stateTypes.Count == 0 || viewModelTypes.Count == 0)
            {
                return;
            }

            var diagnosticsEnabled = IsDiagnosticsEnabled(compilation);
            var mappings = BuildMappings(compilation, stateTypes, viewModelTypes, diagnosticsEnabled, diagnosticsEnabled ? context.ReportDiagnostic : null);
            if (mappings.Count == 0)
            {
                return;
            }

            var source = GenerateSourceText(mappings);
            context.AddSource("MviStateMapper.g.cs", SourceText.From(source, Encoding.UTF8));
        }

        private static List<TypeMapping> BuildMappings(
            Compilation compilation,
            IReadOnlyCollection<INamedTypeSymbol> stateTypes,
            IReadOnlyCollection<INamedTypeSymbol> viewModelTypes,
            bool diagnosticsEnabled,
            Action<Diagnostic>? reportDiagnostic)
        {
            var mappings = new List<TypeMapping>();
            var statePropertyMaps = new Dictionary<INamedTypeSymbol, PropertyMap>(SymbolEqualityComparer.Default);
            var viewModelPropertyMaps = new Dictionary<INamedTypeSymbol, PropertyMap>(SymbolEqualityComparer.Default);

            foreach (var state in stateTypes)
            {
                var stateMap = GetOrCreateStatePropertyMap(statePropertyMaps, state, diagnosticsEnabled, reportDiagnostic);

                foreach (var viewModel in viewModelTypes)
                {
                    var viewModelMap = GetOrCreateViewModelPropertyMap(viewModelPropertyMaps, viewModel, diagnosticsEnabled, reportDiagnostic);
                    var pairs = MatchProperties(compilation, stateMap.Properties, viewModelMap.Properties);
                    if (pairs.Count == 0)
                        continue;

                    mappings.Add(new TypeMapping(state, viewModel, pairs));
                }
            }

            // 优先匹配更具体的派生类型，保证最精确的映射优先命中。
            return mappings
                .OrderByDescending(m => GetInheritanceDepth(m.StateType))
                .ThenByDescending(m => GetInheritanceDepth(m.ViewModelType))
                .ToList();
        }

        private static int GetInheritanceDepth(INamedTypeSymbol type)
        {
            var depth = 0;
            var current = type.BaseType;
            while (current != null)
            {
                depth++;
                current = current.BaseType;
            }
            return depth;
        }

        private static List<PropertyPair> MatchProperties(
            Compilation compilation,
            IReadOnlyDictionary<string, IPropertySymbol> stateProps,
            IReadOnlyDictionary<string, IPropertySymbol> viewModelProps)
        {
            var pairs = new List<PropertyPair>();

            foreach (var kvp in stateProps)
            {
                if (!viewModelProps.TryGetValue(kvp.Key, out var vmProp))
                    continue;

                var stateProp = kvp.Value;
                var conversion = compilation.ClassifyConversion(stateProp.Type, vmProp.Type);
                if (!conversion.Exists || !(conversion.IsIdentity || conversion.IsImplicit))
                    continue;

                pairs.Add(new PropertyPair(stateProp, vmProp));
            }

            return pairs;
        }

        private static PropertyMap GetOrCreateStatePropertyMap(
            Dictionary<INamedTypeSymbol, PropertyMap> cache,
            INamedTypeSymbol type,
            bool diagnosticsEnabled,
            Action<Diagnostic>? reportDiagnostic)
        {
            return GetOrCreatePropertyMap(cache, type, diagnosticsEnabled, reportDiagnostic, ShouldIncludeStateProperty);
        }

        private static PropertyMap GetOrCreateViewModelPropertyMap(
            Dictionary<INamedTypeSymbol, PropertyMap> cache,
            INamedTypeSymbol type,
            bool diagnosticsEnabled,
            Action<Diagnostic>? reportDiagnostic)
        {
            return GetOrCreatePropertyMap(cache, type, diagnosticsEnabled, reportDiagnostic, ShouldIncludeViewModelProperty);
        }

        private static PropertyMap GetOrCreatePropertyMap(
            Dictionary<INamedTypeSymbol, PropertyMap> cache,
            INamedTypeSymbol type,
            bool diagnosticsEnabled,
            Action<Diagnostic>? reportDiagnostic,
            Func<IPropertySymbol, bool> predicate)
        {
            if (cache.TryGetValue(type, out var map))
                return map;

            var properties = new Dictionary<string, IPropertySymbol>(PropertyNameComparer);
            foreach (var property in GetAllPublicProperties(type))
            {
                if (!predicate(property))
                    continue;

                if (!properties.TryAdd(property.Name, property))
                {
                    if (diagnosticsEnabled && reportDiagnostic is not null)
                    {
                        var existing = properties[property.Name];
                        if (!string.Equals(existing.Name, property.Name, StringComparison.Ordinal))
                        {
                            var location = property.Locations.FirstOrDefault();
                            reportDiagnostic(Diagnostic.Create(
                                CaseInsensitiveConflictDescriptor,
                                location,
                                type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                                existing.Name,
                                property.Name));
                        }
                    }
                }
            }

            map = new PropertyMap(properties);
            cache[type] = map;
            return map;
        }

        private static bool ShouldIncludeStateProperty(IPropertySymbol property)
        {
            return property.GetMethod is { DeclaredAccessibility: Accessibility.Public }
                && property.Name != "IsUpdateNewState";
        }

        private static bool ShouldIncludeViewModelProperty(IPropertySymbol property)
        {
            return property.SetMethod is { DeclaredAccessibility: Accessibility.Public } setMethod && !setMethod.IsInitOnly;
        }

        private static IEnumerable<IPropertySymbol> GetAllPublicProperties(INamedTypeSymbol type)
        {
            for (var current = type; current != null; current = current.BaseType)
            {
                foreach (var property in current.GetMembers().OfType<IPropertySymbol>())
                {
                    if (property.IsImplicitlyDeclared)
                        continue;

                    if (property.DeclaredAccessibility != Accessibility.Public)
                        continue;

                    if (property.IsStatic)
                        continue;

                    if (property.IsIndexer)
                        continue;

                    yield return property;
                }
            }
        }

        private static List<INamedTypeSymbol> CollectTypes(
            Compilation compilation,
            IReadOnlyCollection<TypeDeclarationSyntax> candidates,
            Func<INamedTypeSymbol, bool> predicate)
        {
            var results = new List<INamedTypeSymbol>();
            var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            var models = new Dictionary<SyntaxTree, SemanticModel>();

            foreach (var candidate in candidates)
            {
                if (!models.TryGetValue(candidate.SyntaxTree, out var model))
                {
                    model = compilation.GetSemanticModel(candidate.SyntaxTree);
                    models.Add(candidate.SyntaxTree, model);
                }

                if (model.GetDeclaredSymbol(candidate) is not INamedTypeSymbol symbol)
                    continue;

                if (predicate(symbol) && seen.Add(symbol))
                {
                    results.Add(symbol);
                }
            }

            return results;
        }

        private static bool ImplementsInterface(INamedTypeSymbol type, INamedTypeSymbol interfaceSymbol)
        {
            return type.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, interfaceSymbol));
        }

        private static bool InheritsFrom(INamedTypeSymbol type, INamedTypeSymbol baseType)
        {
            for (var current = type; current != null; current = current.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(current, baseType))
                    return true;
            }
            return false;
        }

        private static bool IsDiagnosticsEnabled(Compilation compilation)
        {
            if (compilation is CSharpCompilation csharp)
            {
                return csharp.Options.PreprocessorSymbolNames.Contains(DiagnosticsSymbol);
            }

            return false;
        }

        private static string GenerateSourceText(IReadOnlyList<TypeMapping> mappings)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated>");
            sb.AppendLine("// 由 MapperSourceGenerator 生成，用于同步 IState -> MviViewModel 属性。");
            sb.AppendLine("// 请不要手动修改此文件，修改源类型或生成器。");
            sb.AppendLine("// </auto-generated>");
            sb.AppendLine("#nullable enable");
            sb.AppendLine("namespace MVI.Generated");
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>");
            sb.AppendLine("    /// 编译期状态映射器：根据 IsUpdateNewState 决定增量更新或全量更新。\");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine("    internal static class GeneratedStateMapper");
            sb.AppendLine("    {");
            sb.AppendLine("        public static bool TryMap(global::MVI.IState state, global::MVI.MviViewModel viewModel)");
            sb.AppendLine("        {");
            sb.AppendLine("            if (state is null || viewModel is null)");
            sb.AppendLine("                return false;");
            sb.AppendLine();

            for (var i = 0; i < mappings.Count; i++)
            {
                var map = mappings[i];
                var stateTypeName = map.StateType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var vmTypeName = map.ViewModelType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var stateVar = $"typedState{i}";
                var vmVar = $"typedVm{i}";

                sb.AppendLine($"            if (state is {stateTypeName} {stateVar} && viewModel is {vmTypeName} {vmVar})");
                sb.AppendLine("            {");
                sb.AppendLine($"                Map_{map.StateIdentifier}_{map.ViewModelIdentifier}({stateVar}, {vmVar});");
                sb.AppendLine("                return true;");
                sb.AppendLine("            }");
            }

            sb.AppendLine("            return false;");
            sb.AppendLine("        }");
            sb.AppendLine();

            foreach (var map in mappings)
            {
                var stateTypeName = map.StateType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var vmTypeName = map.ViewModelType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

                sb.AppendLine("        /// <summary>");
                sb.AppendLine($"        /// 将 {map.StateType.Name} 的公共属性复制到 {map.ViewModelType.Name}。\");
                sb.AppendLine("        /// </summary>");
                sb.AppendLine($"        private static void Map_{map.StateIdentifier}_{map.ViewModelIdentifier}({stateTypeName} state, {vmTypeName} viewModel)");
                sb.AppendLine("        {");
                sb.AppendLine("            // onlyIfChanged=true 表示只在值变化时更新。");
                sb.AppendLine("            var onlyIfChanged = !state.IsUpdateNewState;");

                foreach (var pair in map.PropertyPairs)
                {
                    var statePropName = pair.StateProperty.Name;
                    var vmPropName = pair.ViewModelProperty.Name;
                    var typeName = pair.ViewModelProperty.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    var localName = $"newValue_{vmPropName}";

                    sb.AppendLine($"            var {localName} = state.{statePropName};");
                    sb.AppendLine($"            if (!onlyIfChanged || !global::System.Collections.Generic.EqualityComparer<{typeName}>.Default.Equals(viewModel.{vmPropName}, {localName}))");
                    sb.AppendLine("            {");
                    sb.AppendLine($"                viewModel.{vmPropName} = {localName};");
                    sb.AppendLine("            }");
                }

                sb.AppendLine("        }");
                sb.AppendLine();
            }

            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
        }

        private class CandidateReceiver : ISyntaxReceiver
        {
            public List<TypeDeclarationSyntax> TypeCandidates { get; } = new();

            public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
            {
                if (syntaxNode is TypeDeclarationSyntax tds &&
                    (tds.Kind() == SyntaxKind.ClassDeclaration ||
                     tds.Kind() == SyntaxKind.StructDeclaration ||
                     tds.Kind() == SyntaxKind.RecordDeclaration ||
                     tds.Kind() == SyntaxKind.RecordStructDeclaration))
                {
                    TypeCandidates.Add(tds);
                }
            }
        }

        private class PropertyPair
        {
            public PropertyPair(IPropertySymbol stateProperty, IPropertySymbol viewModelProperty)
            {
                StateProperty = stateProperty;
                ViewModelProperty = viewModelProperty;
            }

            public IPropertySymbol StateProperty { get; }
            public IPropertySymbol ViewModelProperty { get; }
        }

        private class PropertyMap
        {
            public PropertyMap(IReadOnlyDictionary<string, IPropertySymbol> properties)
            {
                Properties = properties;
            }

            public IReadOnlyDictionary<string, IPropertySymbol> Properties { get; }
        }

        private class TypeMapping
        {
            public TypeMapping(INamedTypeSymbol stateType, INamedTypeSymbol viewModelType, IReadOnlyList<PropertyPair> propertyPairs)
            {
                StateType = stateType;
                ViewModelType = viewModelType;
                PropertyPairs = propertyPairs;
            }

            public INamedTypeSymbol StateType { get; }
            public INamedTypeSymbol ViewModelType { get; }
            public IReadOnlyList<PropertyPair> PropertyPairs { get; }

            public string StateIdentifier => MakeSafeIdentifier(StateType);
            public string ViewModelIdentifier => MakeSafeIdentifier(ViewModelType);

            private static string MakeSafeIdentifier(INamedTypeSymbol type)
            {
                var name = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var sanitized = SanitizeIdentifier(name);
                var hash = GetStableHashCode(name);
                return $"{sanitized}_{hash:X8}";
            }

            private static string SanitizeIdentifier(string name)
            {
                var sb = new StringBuilder(name.Length);
                foreach (var ch in name)
                {
                    if (char.IsLetterOrDigit(ch) || ch == '_')
                    {
                        sb.Append(ch);
                    }
                    else
                    {
                        sb.Append('_');
                    }
                }

                if (sb.Length == 0 || !(char.IsLetter(sb[0]) || sb[0] == '_'))
                {
                    sb.Insert(0, '_');
                }

                return sb.ToString();
            }

            private static int GetStableHashCode(string text)
            {
                unchecked
                {
                    var hash = 23;
                    foreach (var ch in text)
                    {
                        hash = (hash * 31) + ch;
                    }

                    return hash;
                }
            }
        }
    }
}
