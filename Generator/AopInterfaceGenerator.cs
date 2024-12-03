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
    [Generator]
    public class AopInterfaceGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var classDeclarations = AnalizeHelper.DefiningFilter(context);
            var compilationAndClasses = AnalizeHelper.GetValue(context, classDeclarations);
            context.RegisterSourceOutput(compilationAndClasses, GenerateSource);
        }
        private static void GenerateSource(SourceProductionContext context, (Compilation Compilation, ImmutableArray<ClassDeclarationSyntax> Classes) input)
        {
            foreach (var classDeclaration in input.Classes)
            {
                if (!AnalizeHelper.IsAopClass(classDeclaration)) continue;
                string interfaceName = AnalizeHelper.GetInterfaceName(classDeclaration);
                var baseList = SyntaxFactory.BaseList(
                    SyntaxFactory.SingletonSeparatedList<BaseTypeSyntax>(
                        SyntaxFactory.SimpleBaseType(
                            SyntaxFactory.QualifiedName(
                                SyntaxFactory.ParseName("MinimalisticWPF"),
                                SyntaxFactory.IdentifierName("IProxy")))));
                var interfaceDeclaration = SyntaxFactory.InterfaceDeclaration(interfaceName)
                    .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
                    .WithBaseList(baseList);

                foreach (var field in classDeclaration.Members.OfType<FieldDeclarationSyntax>())
                {
                    if (!AnalizeHelper.IsVMField(field)) continue;
                    foreach (var variable in field.Declaration.Variables)
                    {
                        var propertyName = AnalizeHelper.GetPropertyNameByFieldName(variable);
                        TypeSyntax propertyType = field.Declaration.Type;

                        var propertyDeclaration = SyntaxFactory.PropertyDeclaration(propertyType, propertyName)
                            .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
                            .WithAccessorList(
                                SyntaxFactory.AccessorList(
                                    SyntaxFactory.List(
                                    [
                                        SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                                        .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
                                        SyntaxFactory.AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
                                        .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
                                    ])));

                        interfaceDeclaration = interfaceDeclaration.AddMembers(propertyDeclaration);
                    }
                }

                foreach (var property in classDeclaration.Members.OfType<PropertyDeclarationSyntax>().Where(p => p.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword))))
                {
                    TypeSyntax propertyType = property.Type;
                    var prop = SyntaxFactory.PropertyDeclaration(propertyType, property.Identifier)
                        .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)));
                    if (property.AccessorList?.Accessors.Any(a => a.Kind() == SyntaxKind.GetAccessorDeclaration) == true)
                    {
                        prop = prop.AddAccessorListAccessors(
                            SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)));
                    }
                    if (property.AccessorList?.Accessors.Any(a => a.Kind() == SyntaxKind.SetAccessorDeclaration) == true)
                    {
                        prop = prop.AddAccessorListAccessors(
                            SyntaxFactory.AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
                                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)));
                    }
                    interfaceDeclaration = interfaceDeclaration.AddMembers(prop);
                }

                foreach (var method in classDeclaration.Members.OfType<MethodDeclarationSyntax>().Where(m => m.Modifiers.Any(mod => mod.IsKind(SyntaxKind.PublicKeyword))))
                {
                    TypeSyntax returnType = method.ReturnType;
                    ParameterListSyntax parameterList = method.ParameterList;
                    var methodSignature = SyntaxFactory.MethodDeclaration(returnType, method.Identifier)
                        .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
                        .WithParameterList(parameterList)
                        .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));
                    interfaceDeclaration = interfaceDeclaration.AddMembers(methodSignature);
                }

                NamespaceDeclarationSyntax namespaceDeclaration = SyntaxFactory.NamespaceDeclaration(SyntaxFactory.ParseName("MinimalisticWPF.AopInterfaces"))
                    .AddMembers(interfaceDeclaration);
                string generatedCode = namespaceDeclaration.NormalizeWhitespace().ToFullString();
                context.AddSource($"{interfaceName}.g.cs", SourceText.From(generatedCode, Encoding.UTF8));
            }
        }
    }
}
