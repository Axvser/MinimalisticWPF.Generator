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
        protected const string NAMESPACE_ITHEME = "global::MinimalisticWPF.StructuralDesign.Theme.";
        protected const string NAMESPACE_TRANSITOIN = "global::MinimalisticWPF.TransitionSystem.";
        protected const string NAMESPACE_THEME = "global::MinimalisticWPF.Theme.";
        protected const string NAMESPACE_MODEL = "global::System.ComponentModel.";
        protected const string NAMESPACE_PROXYEX = "global::MinimalisticWPF.AspectOriented.ProxyExtension.";

        protected const string FULLNAME_MONOCONFIG = "global::MinimalisticWPF.SourceGeneratorMark.MonoBehaviourAttribute";
        protected const string FULLNAME_CONSTRUCTOR = "global::MinimalisticWPF.SourceGeneratorMark.ConstructorAttribute";

        internal ClassRoslyn(ClassDeclarationSyntax classDeclarationSyntax, INamedTypeSymbol namedTypeSymbol, Compilation compilation)
        {
            Syntax = classDeclarationSyntax;
            Symbol = namedTypeSymbol;
            Compilation = compilation;

            IsAop = AnalizeHelper.IsAopClass(classDeclarationSyntax);
            ReadMonoConfig(namedTypeSymbol);
        }

        public ClassDeclarationSyntax Syntax { get; set; }
        public INamedTypeSymbol Symbol { get; set; }
        public Compilation Compilation { get; set; }

        public bool IsAop { get; set; } = false;

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
        public abstract string GetMonoUpdateBody();
        public abstract string GetMonoAwakeBody();
    }
}
