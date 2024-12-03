using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Text;

namespace MinimalisticWPF.Generator
{
    internal static class BuildHelper
    {
        internal static void GenerateUsing(this StringBuilder sourceBuilder, bool isAop)
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
        internal static void GenerateNamespace(this StringBuilder sourceBuilder, INamedTypeSymbol classSymbol)
        {
            sourceBuilder.AppendLine($"namespace {classSymbol.ContainingNamespace}");
            sourceBuilder.AppendLine("{");
        }
        internal static void GenerateProperty(this StringBuilder sourceBuilder, IFieldSymbol field)
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
        internal static void GenerateVMClassPartialClass(this StringBuilder sourceBuilder, ClassDeclarationSyntax cs, bool isAop)
        {
            var option = isAop ? $" , {AnalizeHelper.GetInterfaceName(cs)}" : string.Empty;
            var source = $$"""
                           {{cs.Modifiers}} class {{cs.Identifier.Text}} : INotifyPropertyChanged {{option}}
                           {
                           """;
            sourceBuilder.AppendLine(source);
        }
        internal static void GenerateAopClassPartialClass(this StringBuilder sourceBuilder, ClassDeclarationSyntax cs)
        {
            var source = $$"""
                           {{cs.Modifiers}} class {{cs.Identifier.Text}}
                           {
                           """;
            sourceBuilder.AppendLine(source);
        }
        internal static void GenerateConstruction(this StringBuilder sourceBuilder, ClassDeclarationSyntax cs, INamedTypeSymbol classSymbol, bool isAop)
        {
            var aop = isAop ? $"IAop{cs.Identifier.Text}In{classSymbol.ContainingNamespace.ToString().Replace('.', '_')}" : string.Empty;
            sourceBuilder.AppendLine($"   public {cs.Identifier.Text} ()");
            sourceBuilder.AppendLine("   {");
            sourceBuilder.AppendLine(isAop ? $"      Proxy = this.CreateProxy<{aop}>();" : string.Empty);
            sourceBuilder.AppendLine("   }");
        }
        internal static void GenerateIPC(this StringBuilder sourceBuilder)
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
        internal static void GenerateProxy(this StringBuilder sourceBuilder, ClassDeclarationSyntax cs, INamedTypeSymbol classSymbol, bool isAop)//动态代理
        {
            if (!isAop) return;
            var aop = isAop ? $"IAop{cs.Identifier.Text}In{classSymbol.ContainingNamespace.ToString().Replace('.', '_')}" : string.Empty;
            sourceBuilder.AppendLine($$"""
                                          public {{aop}} Proxy { get; private set; }
                                       """);
        }
        internal static void GenerateEnd(this StringBuilder sourceBuilder)
        {
            sourceBuilder.AppendLine("}");
            sourceBuilder.AppendLine("}");
        }
        internal static void AddVMClassSource(this SourceProductionContext context, INamedTypeSymbol classSymbol, ClassDeclarationSyntax classDeclaration, StringBuilder stringBuilder)
        {
            context.AddSource($"{classSymbol.ContainingNamespace.ToString().Replace('.', '_')}_{classDeclaration.Identifier.Text}_VMClass.g.cs", SourceText.From(stringBuilder.ToString(), Encoding.UTF8));
        }
        internal static void AddAopClassSource(this SourceProductionContext context, INamedTypeSymbol classSymbol, ClassDeclarationSyntax classDeclaration, StringBuilder stringBuilder)
        {
            context.AddSource($"{classSymbol.ContainingNamespace.ToString().Replace('.', '_')}_{classDeclaration.Identifier.Text}_AopClass.g.cs", SourceText.From(stringBuilder.ToString(), Encoding.UTF8));
        }
    }
}
