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
    public class VMGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            IncrementalValuesProvider<ClassDeclarationSyntax> classDeclarations =
                context.SyntaxProvider.CreateSyntaxProvider(
                    predicate: static (node, cancellationToken) => IsPartialClass(node),
                    transform: static (context, cancellationToken) => GetClassDeclaration(context))
                .Where(static m => m != null)!;

            IncrementalValueProvider<(Compilation Compilation, ImmutableArray<ClassDeclarationSyntax> Classes)> compilationAndClasses =
                context.CompilationProvider.Combine(classDeclarations.Collect());

            context.RegisterSourceOutput(compilationAndClasses, GenerateSource);
        }

        private static bool IsPartialClass(SyntaxNode node)//筛查:分部类型
        {
            return node is ClassDeclarationSyntax classDecl && classDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword));
        }
        private static ClassDeclarationSyntax GetClassDeclaration(GeneratorSyntaxContext context)//获取:类型声明
        {
            var classDeclaration = (ClassDeclarationSyntax)context.Node;
            return classDeclaration;
        }

        private static void GenerateSource(SourceProductionContext context, (Compilation Compilation, ImmutableArray<ClassDeclarationSyntax> Classes) input)
        {
            var (compilation, classes) = input;

            Dictionary<Tuple<INamespaceSymbol, ClassDeclarationSyntax>, StringBuilder> generatedSources = [];

            foreach (var classDeclaration in classes)
            {
                SemanticModel model = compilation.GetSemanticModel(classDeclaration.SyntaxTree);
                var classSymbol = model.GetDeclaredSymbol(classDeclaration);
                if (classSymbol == null)
                    continue;

                // 至少有一个字段携带 [Expandable] 特性
                var expandableFields = classSymbol.GetMembers()
                    .OfType<IFieldSymbol>()
                    .Where(field => field.GetAttributes().Any(attr => attr.AttributeClass?.Name == "VMProperty"));
                if (!expandableFields.Any())
                    continue;

                if (!generatedSources.TryGetValue(Tuple.Create(classSymbol.ContainingNamespace, classDeclaration), out var sourceBuilder))
                {
                    sourceBuilder = new StringBuilder();
                    sourceBuilder.AppendLine($"// <auto-generated/>");
                    GenerateUsing(sourceBuilder, HasAspectOrientedAttribute(classDeclaration));
                    GenerateNamespace(sourceBuilder, classSymbol);
                    GeneratePartialClass(sourceBuilder, classDeclaration);
                    GenerateIPC(sourceBuilder);
                    GenerateConstruction(sourceBuilder, classDeclaration, classSymbol, HasAspectOrientedAttribute(classDeclaration));
                    generatedSources[Tuple.Create(classSymbol.ContainingNamespace, classDeclaration)] = sourceBuilder;
                }

                foreach (var item in expandableFields)
                {
                    GenerateProperty(sourceBuilder, item);
                }
            }
            foreach (var kvp in generatedSources)
            {
                kvp.Value.AppendLine("}");
                kvp.Value.AppendLine("}");
                context.AddSource($"{kvp.Key.Item1.MetadataName.Replace('.','_')}_{kvp.Key.Item2.Identifier.Text}_VM.g.cs", SourceText.From(kvp.Value.ToString(), Encoding.UTF8));
            }
        }
        private static void GenerateUsing(StringBuilder sourceBuilder, bool isAop)
        {
            sourceBuilder.AppendLine("#nullable enable");
            sourceBuilder.AppendLine();
            sourceBuilder.AppendLine("using System.ComponentModel;");
            sourceBuilder.AppendLine("using MinimalisticWPF;");
            if (isAop)
            {
                sourceBuilder.AppendLine("using MinimalisticWPF.AopInterfaces;");
            }
            sourceBuilder.AppendLine();
        }
        private static void GenerateNamespace(StringBuilder sourceBuilder, INamedTypeSymbol classSymbol)
        {
            sourceBuilder.AppendLine($"namespace {classSymbol.ContainingNamespace}");
            sourceBuilder.AppendLine("{");
        }
        private static void GenerateIPC(StringBuilder sourceBuilder)
        {
            string source = $$"""
                                 public event PropertyChangedEventHandler? PropertyChanged;
                                 public void OnPropertyChanged(string propertyName)
                                 {
                                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
                                 }
                              """;
            sourceBuilder.AppendLine(source);
        }
        private static void GeneratePartialClass(StringBuilder sourceBuilder, ClassDeclarationSyntax cs)
        {
            var source = $$"""
                           {{cs.Modifiers}} class {{cs.Identifier.Text}} : INotifyPropertyChanged
                           {
                           """;
            sourceBuilder.AppendLine(source);
        }
        private static void GenerateConstruction(StringBuilder sourceBuilder, ClassDeclarationSyntax cs, INamedTypeSymbol classSymbol, bool isAop)
        {
            var aop = isAop ? $"IAop{cs.Identifier.Text}In{classSymbol.ContainingNamespace.ToString().Replace('.', '_')}" : string.Empty;
            sourceBuilder.AppendLine($"   public {cs.Identifier.Text} ()");
            sourceBuilder.AppendLine("   {");
            sourceBuilder.AppendLine(isAop ? $"      Proxy = this.CreateProxy<{aop}>();" : string.Empty);
            sourceBuilder.AppendLine("   }");
        }
        private static void GenerateProperty(StringBuilder sourceBuilder, IFieldSymbol field)
        {
            string propertyName = char.ToUpper(field.Name[1]) + field.Name.Substring(2);
            var source = $$"""
                              public {{field.Type}} {{propertyName}}
                              {
                                  get => {{field.Name}} ;
                                  set
                                  {
                                      if(value!={{field.Name}})
                                      {
                                          {{field.Name}} = value;
                                          OnPropertyChanged(nameof({{propertyName}}));
                                      }
                                  }
                              }
                           """;
            sourceBuilder.AppendLine(source);
        }
        internal static bool HasAspectOrientedAttribute(ClassDeclarationSyntax classDeclaration)
        {
            // 获取类上的所有属性列表
            var attributeLists = classDeclaration.AttributeLists;

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
    }
}
