﻿using System;
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
    public class VMClassGenerator : IIncrementalGenerator
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

            Dictionary<Tuple<INamedTypeSymbol, ClassDeclarationSyntax>, StringBuilder> generatedSources = [];

            foreach (var classDeclaration in classes)
            {
                SemanticModel model = compilation.GetSemanticModel(classDeclaration.SyntaxTree);
                var classSymbol = model.GetDeclaredSymbol(classDeclaration);
                if (classSymbol == null)
                    continue;

                if (!AnalizeHelper.IsVMFieldExist(classSymbol, out var vmfields)) continue;
                var isAop = AnalizeHelper.IsAopClass(classDeclaration);
                var conGen = classSymbol.GetConstructorGenerator(isAop);

                if (!generatedSources.TryGetValue(Tuple.Create(classSymbol, classDeclaration), out var sourceBuilder))
                {
                    sourceBuilder = new StringBuilder();
                    sourceBuilder.AppendLine($"// <auto-generated/>");
                    sourceBuilder.GenerateUsing(isAop);
                    sourceBuilder.GenerateNamespace(classSymbol);
                    sourceBuilder.GenerateVMClassPartialClass(classDeclaration, isAop);
                    sourceBuilder.AppendLine(conGen.Constructor);
                    sourceBuilder.GenerateIPC();
                    sourceBuilder.AppendLine();
                    generatedSources[Tuple.Create(classSymbol, classDeclaration)] = sourceBuilder;
                }

                var fc = classSymbol.GetFieldGenerator();

                foreach (var item in fc.VMPropertyGenerations)
                {
                    sourceBuilder.AppendLine(item);
                }
            }
            foreach (var kvp in generatedSources)
            {
                kvp.Value.GenerateEnd();
                context.AddVMClassSource(kvp.Key.Item1, kvp.Key.Item2, kvp.Value);
            }
        }
    }
}
