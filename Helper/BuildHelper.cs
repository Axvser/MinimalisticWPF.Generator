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
        internal static void GenerateVMClassPartialClass(this StringBuilder sourceBuilder, ClassDeclarationSyntax cs, bool isAop, bool isVM)
        {
            string share = $"{cs.Modifiers} class {cs.Identifier.Text}";
            string option = (isVM, isAop) switch
            {
                (true, true) => $" : INotifyPropertyChanged ,{AnalizeHelper.GetInterfaceName(cs)}",
                (true, false) => $" : INotifyPropertyChanged",
                (false, true) => $" : {AnalizeHelper.GetInterfaceName(cs)}",
                _ => string.Empty
            };
            var source = $$"""
                              {{share}}{{option}}
                              {
                           """;
            sourceBuilder.AppendLine(source);
        }
        internal static void GenerateIPC(this StringBuilder sourceBuilder, bool isVM)
        {
            if (!isVM) return;
            string source = $$"""
                                    public event PropertyChangedEventHandler? PropertyChanged;
                                    public void OnPropertyChanged(string propertyName)
                                    {
                                       PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
                                    }
                              """;
            sourceBuilder.AppendLine(source);
        }
        internal static void GenerateEnd(this StringBuilder sourceBuilder)
        {
            sourceBuilder.AppendLine("   }");
            sourceBuilder.AppendLine("}");
        }
        internal static void AddVMClassSource(this SourceProductionContext context, INamedTypeSymbol classSymbol, ClassDeclarationSyntax classDeclaration, StringBuilder stringBuilder)
        {
            context.AddSource($"{classSymbol.ContainingNamespace.ToString().Replace('.', '_')}_{classDeclaration.Identifier.Text}_VMClass.g.cs", SourceText.From(stringBuilder.ToString(), Encoding.UTF8));
        }
    }
}
