﻿using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace MinimalisticWPF.Generator
{
    [Generator]
    public class BackendCodeGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var classDeclarations = AnalizeHelper.DefiningFilter(context);
            var compilationAndClasses = AnalizeHelper.GetValue(context, classDeclarations);
            context.RegisterSourceOutput(compilationAndClasses, GenerateSource);
        }
        private static void GenerateSource(SourceProductionContext context, (Compilation Compilation, ImmutableArray<ClassDeclarationSyntax> Classes) input)
        {
            var (compilation, classes) = input;

            HashSet<INamedTypeSymbol> processedSymbols = [];
            List<ClassDeclarationSyntax> uniqueClasses = [];

            foreach (var classDeclaration in classes)
            {
                SemanticModel model = compilation.GetSemanticModel(classDeclaration.SyntaxTree);
                var classSymbol = model.GetDeclaredSymbol(classDeclaration);
                if (classSymbol == null)
                    continue;

                if (!processedSymbols.Contains(classSymbol))
                {
                    processedSymbols.Add(classSymbol);
                    uniqueClasses.Add(classDeclaration);
                }
            }

            Dictionary<Tuple<INamedTypeSymbol, ClassDeclarationSyntax>, StringBuilder> generatedSources = [];

            foreach (var classDeclaration in uniqueClasses)
            {
                SemanticModel model = compilation.GetSemanticModel(classDeclaration.SyntaxTree);
                var classSymbol = model.GetDeclaredSymbol(classDeclaration);
                if (classSymbol == null)
                    continue;

                var classRoslyn = new ClassRoslyn(classDeclaration, classSymbol);

                if (!(classRoslyn.IsViewModel || classRoslyn.IsAop || classRoslyn.IsDynamicTheme) && !classRoslyn.IsContextConfig) continue;

                if (!generatedSources.TryGetValue(Tuple.Create(classSymbol, classDeclaration), out var sourceBuilder))
                {
                    sourceBuilder = new StringBuilder();
                    sourceBuilder.AppendLine($"// <auto-generated/>");
                    if (classRoslyn.IsContextConfig)
                    {
                        var vm = uniqueClasses.FirstOrDefault(x => x.Identifier.Text == classRoslyn.ViewModelTypeName
                                                                && (string.IsNullOrEmpty(classRoslyn.ViewModelValidation) || compilation.GetSemanticModel(x.SyntaxTree)?.GetDeclaredSymbol(x)?.ContainingNamespace.ToString() == classRoslyn.ViewModelValidation));
                        if (vm != null)
                        {
                            SemanticModel vmModel = compilation.GetSemanticModel(vm.SyntaxTree);
                            var vmsymbol = vmModel.GetDeclaredSymbol(vm);
                            if (vmsymbol != null)
                            {
                                if (!string.IsNullOrEmpty(classRoslyn.ViewModelValidation) && vmsymbol.ContainingNamespace.ToString() != classRoslyn.ViewModelValidation)
                                {
                                    continue;
                                }
                                sourceBuilder.AppendLine(classRoslyn.GenerateUsing(ClassRoslyn.GetReferencedNamespaces(vmsymbol)));
                                sourceBuilder.AppendLine(classRoslyn.GenerateNamespace());
                                sourceBuilder.AppendLine(classRoslyn.GeneratePartialClass());
                                var vmclassRoslyn = new ClassRoslyn(vm, vmsymbol);
                                sourceBuilder.AppendLine(vmclassRoslyn.GenerateDependencyProperties(classRoslyn.Symbol.ContainingNamespace.ToString() + '.' + classRoslyn.Syntax.Identifier.Text, vmsymbol.ContainingNamespace.ToString(), classRoslyn.ViewModelTypeName));
                                sourceBuilder.AppendLine(vmclassRoslyn.GenerateHoverDependencyProperties(classRoslyn.Symbol.ContainingNamespace.ToString() + '.' + classRoslyn.Syntax.Identifier.Text, vmsymbol.ContainingNamespace.ToString(), classRoslyn.ViewModelTypeName));
                                sourceBuilder.AppendLine(classRoslyn.GenerateEnd());
                                generatedSources[Tuple.Create(classSymbol, classDeclaration)] = sourceBuilder;
                            }
                        }
                    }
                    else
                    {
                        sourceBuilder.AppendLine(classRoslyn.GenerateUsing());
                        sourceBuilder.AppendLine(classRoslyn.GenerateNamespace());
                        sourceBuilder.AppendLine(classRoslyn.GeneratePartialClass());
                        sourceBuilder.AppendLine(classRoslyn.GenerateConstructor());
                        sourceBuilder.AppendLine(classRoslyn.GenerateIPC());
                        sourceBuilder.AppendLine(classRoslyn.GenerateITA());
                        sourceBuilder.AppendLine(classRoslyn.GenerateIPA());
                        foreach (var fieldRoslyn in classRoslyn.FieldRoslyns)
                        {
                            sourceBuilder.AppendLine(fieldRoslyn.GenerateCode());
                        }
                        sourceBuilder.AppendLine(classRoslyn.GenerateHoverControl());
                        sourceBuilder.AppendLine(classRoslyn.GenerateEnd());
                        generatedSources[Tuple.Create(classSymbol, classDeclaration)] = sourceBuilder;
                    }
                }
            }

            foreach (var kvp in generatedSources)
            {
                context.AddSource($"{kvp.Key.Item1.ContainingNamespace.ToString().Replace('.', '_')}_{kvp.Key.Item2.Identifier.Text}_FastBackendCodeGeneration.g.cs", SourceText.From(kvp.Value.ToString(), Encoding.UTF8));
            }
        }
    }
}
