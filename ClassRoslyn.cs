using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace MinimalisticWPF.Generator
{
    internal abstract class ClassRoslyn
    {
        protected const string NAMESPACE_AOP = "global::MinimalisticWPF.AopInterfaces.";
        protected const string NAMESPACE_THEME = "global::MinimalisticWPF.StructuralDesign.Theme.";
        protected const string FULLNAME_MONOCONFIG = "global::MinimalisticWPF.MonoBehaviourAttribute";

        internal ClassRoslyn(ClassDeclarationSyntax classDeclarationSyntax, INamedTypeSymbol namedTypeSymbol, Compilation compilation)
        {
            Syntax = classDeclarationSyntax;
            Symbol = namedTypeSymbol;
            Compilation = compilation;

            IsAop = AnalizeHelper.IsAopClass(Syntax);
            IsDynamicTheme = AnalizeHelper.IsThemeClass(Symbol, out var headThemes, out var bodyThemes);
            ReadMonoConfig(namedTypeSymbol);
        }

        public ClassDeclarationSyntax Syntax { get; set; }
        public INamedTypeSymbol Symbol { get; set; }
        public Compilation Compilation { get; set; }

        public bool IsAop { get; set; } = false;
        public bool IsDynamicTheme { get; set; } = false;

        public bool IsMono { get; set; } = false;
        public double MonoSpan { get; set; } = 17;
        private void ReadMonoConfig(INamedTypeSymbol symbol)
        {
            var attributeData = symbol.GetAttributes()
                .FirstOrDefault(ad => ad.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == FULLNAME_MONOCONFIG);
            if (attributeData != null)
            {
                IsMono = true;
                MonoSpan = (double)attributeData.ConstructorArguments[0].Value!;
            }
        }

        public string GenerateUsing()
        {
            StringBuilder sourceBuilder = new();
            if (IsAop)
            {
                sourceBuilder.AppendLine("using global::MinimalisticWPF;");
            }
            sourceBuilder.AppendLine("using global::MinimalisticWPF.TransitionSystem;");
            sourceBuilder.AppendLine();
            sourceBuilder.AppendLine("#nullable enable");
            sourceBuilder.AppendLine();
            return sourceBuilder.ToString();
        }
        public string GenerateNamespace()
        {
            StringBuilder sourceBuilder = new();
            sourceBuilder.AppendLine($"namespace {Symbol.ContainingNamespace}");
            sourceBuilder.AppendLine("{");
            return sourceBuilder.ToString();
        }
        public string GenerateEnd()
        {
            StringBuilder sourceBuilder = new();
            sourceBuilder.AppendLine("   }");
            sourceBuilder.AppendLine("}");
            return sourceBuilder.ToString();
        }
    }
}
