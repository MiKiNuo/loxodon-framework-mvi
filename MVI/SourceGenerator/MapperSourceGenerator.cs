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
    /// Emits strongly typed mappers from IState implementations to MviViewModel derivatives to avoid runtime reflection.
    /// </summary>
    [Generator]
    public class MapperSourceGenerator : ISourceGenerator
    {
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

            var mappings = BuildMappings(compilation, stateTypes, viewModelTypes);
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
            IReadOnlyCollection<INamedTypeSymbol> viewModelTypes)
        {
            var mappings = new List<TypeMapping>();

            foreach (var state in stateTypes)
            {
                foreach (var viewModel in viewModelTypes)
                {
                    var pairs = MatchProperties(compilation, state, viewModel);
                    if (pairs.Count == 0)
                        continue;

                    mappings.Add(new TypeMapping(state, viewModel, pairs));
                }
            }

            // Prefer the most derived state types first so specific matches win.
            return mappings
                .OrderByDescending(m => GetInheritanceDepth(m.StateType))
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
            INamedTypeSymbol state,
            INamedTypeSymbol viewModel)
        {
            var pairs = new List<PropertyPair>();

            var stateProps = GetAllPublicProperties(state)
                .Where(p => p.GetMethod is not null && p.Name != "IsUpdateNewState")
                .ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

            var vmProps = GetAllPublicProperties(viewModel)
                .Where(p => p.SetMethod is not null)
                .ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

            foreach (var kvp in stateProps)
            {
                if (!vmProps.TryGetValue(kvp.Key, out var vmProp))
                    continue;

                var stateProp = kvp.Value;
                var conversion = compilation.ClassifyConversion(stateProp.Type, vmProp.Type);
                if (!conversion.Exists || !(conversion.IsIdentity || conversion.IsImplicit))
                    continue;

                pairs.Add(new PropertyPair(stateProp, vmProp));
            }

            return pairs;
        }

        private static IEnumerable<IPropertySymbol> GetAllPublicProperties(INamedTypeSymbol type)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            for (var current = type; current != null; current = current.BaseType)
            {
                foreach (var property in current.GetMembers().OfType<IPropertySymbol>())
                {
                    if (property.IsImplicitlyDeclared)
                        continue;

                    if (property.DeclaredAccessibility != Accessibility.Public)
                        continue;

                    if (!names.Add(property.Name))
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

            foreach (var candidate in candidates)
            {
                var model = compilation.GetSemanticModel(candidate.SyntaxTree);
                if (model.GetDeclaredSymbol(candidate) is not INamedTypeSymbol symbol)
                    continue;

                if (predicate(symbol))
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

        private static string GenerateSourceText(IReadOnlyList<TypeMapping> mappings)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated>");
            sb.AppendLine("// Generated by MapperSourceGenerator to sync IState -> MviViewModel properties.");
            sb.AppendLine("// Do not edit this file; change source types or the generator instead.");
            sb.AppendLine("// </auto-generated>");
            sb.AppendLine("#nullable enable");
            sb.AppendLine("namespace MVI.Generated");
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>");
            sb.AppendLine("    /// Compile-time state mapper that uses IsUpdateNewState to choose conditional or full updates.");
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
                sb.AppendLine($"        /// Copy public properties from {map.StateType.Name} into {map.ViewModelType.Name}.");
                sb.AppendLine("        /// </summary>");
                sb.AppendLine($"        private static void Map_{map.StateIdentifier}_{map.ViewModelIdentifier}({stateTypeName} state, {vmTypeName} viewModel)");
                sb.AppendLine("        {");
                sb.AppendLine("            // onlyIfChanged=true updates only when values differ, matching the previous runtime logic.");
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
                    (tds.Kind() == SyntaxKind.ClassDeclaration || tds.Kind() == SyntaxKind.RecordDeclaration))
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
                var name = type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                return name.Replace('.', '_').Replace('+', '_');
            }
        }
    }
}
