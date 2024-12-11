using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

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
            var attributeLists = classDecl.AttributeLists;
            foreach (var attributeList in attributeLists)
            {
                foreach (var attribute in attributeList.Attributes)
                {
                    if (attribute.Name.ToString() == "AspectOriented")
                    {
                        return true;
                    }
                }
            }
            return false;
        }
        internal static bool IsDynamicTheme(ClassDeclarationSyntax classDecl)
        {
            var attributeLists = classDecl.AttributeLists;
            foreach (var attributeList in attributeLists)
            {
                foreach (var attribute in attributeList.Attributes)
                {
                    if (attribute.Name.ToString() == "Theme")
                    {
                        return true;
                    }
                }
            }
            return false;
        }
        internal static bool IsVMFieldExist(INamedTypeSymbol classSymbol, out IEnumerable<IFieldSymbol> fieldSymbols)
        {
            fieldSymbols = classSymbol.GetMembers()
                    .OfType<IFieldSymbol>()
                    .Where(field => field.GetAttributes().Any(attr => attr.AttributeClass?.Name == "VMPropertyAttribute"));
            return fieldSymbols.Any();
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
            while (current != null && !(current is NamespaceDeclarationSyntax))
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
            return char.ToUpper(variable.Identifier.Text[1]) + variable.Identifier.Text.Substring(2);
        }
    }
}
