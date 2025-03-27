using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MinimalisticWPF.Generator
{
    internal static class AnalizeHelper
    {
        internal static bool IsPartialClass(SyntaxNode node)
        {
            return node is ClassDeclarationSyntax classDecl && classDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword));
        }
        internal static bool IsAopClass(ClassDeclarationSyntax classDecl)
        {
            return classDecl.Members
                .OfType<MemberDeclarationSyntax>()
                .Any(member => member.AttributeLists
                    .SelectMany(al => al.Attributes)
                    .Any(attr => attr.Name.ToString() == "AspectOriented"));
        }
        internal static bool FindAttribute(ClassDeclarationSyntax classDecl, string attributeName)
        {
            return classDecl.Members
                .OfType<MemberDeclarationSyntax>()
                .Any(member => member.AttributeLists
                    .SelectMany(al => al.Attributes)
                    .Any(attr => attr.Name.ToString() == attributeName));
        }
        internal static bool FindAttribute(IFieldSymbol fieldSymbol, string attributeName)
        {
            foreach (var attribute in fieldSymbol.GetAttributes())
            {
                if (attribute.AttributeClass == null) continue;

                if (attribute.AttributeClass.AllInterfaces.Any(i => i.Name == attributeName))
                {
                    return true;
                }
            }
            return false;
        }

        internal static ClassDeclarationSyntax GetClassDeclaration(GeneratorSyntaxContext context)
        {
            var classDeclaration = (ClassDeclarationSyntax)context.Node;
            return classDeclaration;
        }
        internal static IncrementalValuesProvider<ClassDeclarationSyntax> DefiningFilter(IncrementalGeneratorInitializationContext context)
        {
            IncrementalValuesProvider<ClassDeclarationSyntax> classDeclarations =
                context.SyntaxProvider.CreateSyntaxProvider(
                    predicate: static (node, cancellationToken) => AnalizeHelper.IsPartialClass(node),
                    transform: static (context, cancellationToken) => AnalizeHelper.GetClassDeclaration(context))
                .Where(static m => m != null)!;
            return classDeclarations;
        }
        internal static IncrementalValueProvider<(Compilation Compilation, ImmutableArray<ClassDeclarationSyntax> Classes)> GetValue(IncrementalGeneratorInitializationContext context, IncrementalValuesProvider<ClassDeclarationSyntax> classDeclarations)
        {
            IncrementalValueProvider<(Compilation Compilation, ImmutableArray<ClassDeclarationSyntax> Classes)> compilationAndClasses =
                context.CompilationProvider.Combine(classDeclarations.Collect());
            return compilationAndClasses;
        }
        internal static string GetNamespace(ClassDeclarationSyntax classDeclaration)
        {
            SyntaxNode? current = classDeclaration;
            while (current != null && current is not NamespaceDeclarationSyntax)
            {
                current = current.Parent;
            }

            return current is NamespaceDeclarationSyntax namespaceDeclaration
                ? namespaceDeclaration.Name.ToString()
                : "Global";
        }
        internal static string GetInterfaceName(ClassDeclarationSyntax classDeclarationSyntax)
        {
            return $"IAop{classDeclarationSyntax.Identifier.Text}In{GetNamespace(classDeclarationSyntax).Replace('.', '_')}";
        }
        internal static string GetPropertyNameByFieldName(VariableDeclaratorSyntax variable)
        {
            if (variable.Identifier.Text.StartsWith("_"))
            {
                return char.ToUpper(variable.Identifier.Text[1]) + variable.Identifier.Text.Substring(2);
            }
            else
            {
                return char.ToUpper(variable.Identifier.Text[0]) + variable.Identifier.Text.Substring(1);
            }
        }
        internal static string InitialTextParse(this string source)
        {
            return source.Replace('=', ' ').TrimStart();
        }
        internal static string GetAccessModifier(INamedTypeSymbol symbol)
        {
            switch (symbol.DeclaredAccessibility)
            {
                case Accessibility.Public:
                    return "public";
                case Accessibility.Internal:
                    return "internal";
                case Accessibility.Private:
                    return "private";
                case Accessibility.Protected:
                    return "protected";
                case Accessibility.ProtectedAndInternal:
                    return "protected internal";
                case Accessibility.ProtectedOrInternal:
                    return "private protected";
                default:
                    return "internal";
            }
        }

        internal static string GetFileName(INamedTypeSymbol symbol, ClassDeclarationSyntax classDeclarationSyntax)
        {
            return $"{symbol.ContainingNamespace.ToString().Replace('.', '_')}_{classDeclarationSyntax.Identifier.Text}_FastBackendCodeGeneration.g.cs";
        }
        internal static string ExtractThemeName(string fullAttributeText)
        {
            // 步骤1：分割参数部分
            int firstParen = fullAttributeText.IndexOf('(');
            string withoutArgs = (firstParen >= 0) ?
                fullAttributeText.Substring(0, firstParen) :
                fullAttributeText;

            // 步骤2：定位最后一个命名空间分隔符
            int lastDot = withoutArgs.LastIndexOf('.');
            string candidate = (lastDot >= 0) ?
                withoutArgs.Substring(lastDot + 1) :
                withoutArgs;

            // 步骤3：严格校验名称规范
            candidate = candidate.Trim();
            bool hasInvalidChars = candidate.Contains('.') || candidate.Contains('(');
            return hasInvalidChars ? string.Empty : candidate;
        }
    }
}
