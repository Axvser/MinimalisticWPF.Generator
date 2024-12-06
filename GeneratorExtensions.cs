using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace MinimalisticWPF.Generator
{
    public static class GeneratorExtensions
    {
        public static FieldRoslynGenerator GetFieldGenerator(this INamedTypeSymbol namedTypeSymbol)
        {
            var csMembers = namedTypeSymbol.GetMembers();
            var vmProperties = csMembers.OfType<IFieldSymbol>().Where(f => f.GetAttributes().Any(attr => attr.AttributeClass?.Name == "VMProperty"));
            var generatorParams = vmProperties.Select(p => (p, csMembers.OfType<IMethodSymbol>().Where(m => m.GetAttributes().Any(attr => attr.AttributeClass?.Name == "VMWatcher" && (m.Name.Contains(p.Name) || m.Name.Contains(char.ToUpper(p.Name[1]) + p.Name.Substring(2)))))));
            return new FieldRoslynGenerator(generatorParams);
        }
        public static ConstructorRoslynGenerator GetConstructorGenerator(this INamedTypeSymbol namedTypeSymbol, bool isAop)
        {
            return new ConstructorRoslynGenerator(namedTypeSymbol, isAop);
        }
        public static string GetNameSpaceWithOutDot(this INamedTypeSymbol namedTypeSymbol)
        {
            return namedTypeSymbol.ContainingNamespace.ToString().Replace('.', '_');
        }
        public static string GetModifiers(this INamedTypeSymbol namedTypeSymbol)
        {
            return namedTypeSymbol.GetModifiers();
        }
        public static bool IsAop(this INamedTypeSymbol namedTypeSymbol)
        {
            return namedTypeSymbol.GetAttributes().Any(attr => attr.AttributeClass?.Name == "AspectOriented");
        }
    }
}
