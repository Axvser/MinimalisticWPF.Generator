using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MinimalisticWPF.Generator.Factory;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MinimalisticWPF.Generator
{
    internal class ViewModelRoslyn : ClassRoslyn
    {
        private const string NAMESPACE_MVVM = "global::System.ComponentModel.";
        const string PUBLIC = "public";

        internal ViewModelRoslyn(ClassDeclarationSyntax classDeclarationSyntax, INamedTypeSymbol namedTypeSymbol, Compilation compilation) : base(classDeclarationSyntax, namedTypeSymbol, compilation)
        {
            IsViewModel = AnalizeHelper.IsViewModelClass(Symbol, out var vmfields);
            FieldRoslyns = vmfields.Select(field => new FieldRoslyn(field));
        }

        public bool IsViewModel { get; set; } = false;

        public IEnumerable<FieldRoslyn> FieldRoslyns { get; set; } = [];

        public string Generate()
        {
            var builder = new StringBuilder();

            builder.AppendLine(GenerateUsing());
            builder.AppendLine(GenerateNamespace());
            builder.AppendLine(GeneratePartialClass());
            builder.AppendLine(GenerateIPC());
            builder.AppendLine(GenerateEnd());

            return builder.ToString();
        }
        public string GeneratePartialClass()
        {
            StringBuilder sourceBuilder = new();
            string share = $"{Syntax.Modifiers} class {Syntax.Identifier.Text}";

            var list = new List<string>();
            if (IsViewModel)
            {
                list.Add($"{NAMESPACE_MVVM}INotifyPropertyChanged");
                list.Add($"{NAMESPACE_MVVM}INotifyPropertyChanging");
            }
            if (IsAop)
            {
                list.Add($"{NAMESPACE_AOP}{AnalizeHelper.GetInterfaceName(Syntax)}");
            }
            if (list.Count > 0)
            {
                var result = string.Join(", ", list);
                var source = $$"""
                              {{share}} : {{result}}
                              {
                           """;
                sourceBuilder.AppendLine(source);
            }
            else
            {
                var source = $$"""
                              {{share}}
                              {
                           """;
                sourceBuilder.AppendLine(source);
            }

            return sourceBuilder.ToString();
        }
        public string GenerateIPC()
        {
            StringBuilder sourceBuilder = new();
            string source = $$"""
                                    public event {{NAMESPACE_MVVM}}PropertyChangedEventHandler? PropertyChanged;
                                    public event {{NAMESPACE_MVVM}}PropertyChangingEventHandler? PropertyChanging;
                                    public void OnPropertyChanging(string propertyName)
                                    {
                                       PropertyChanging?.Invoke(this, new {{NAMESPACE_MVVM}}PropertyChangingEventArgs(propertyName));
                                    }
                                    public void OnPropertyChanged(string propertyName)
                                    {
                                       PropertyChanged?.Invoke(this, new {{NAMESPACE_MVVM}}PropertyChangedEventArgs(propertyName));
                                    }

                              """;
            sourceBuilder.AppendLine(source);

            foreach (var field in FieldRoslyns)
            {
                var factory = new PropertyFactory(field, PUBLIC, false);
                var intercept = field.SetterValidation switch
                {
                    1 => $"if (!{field.FieldName}?.Equals(value) ?? false) return;",
                    2 => $"if (!CanUpdate{field.PropertyName}(old,value)) return;",
                    _ => string.Empty
                };
                var interceptmethod = field.SetterValidation switch
                {
                    2 => $"      private partial bool CanUpdate{field.PropertyName}({field.TypeName} oldValue, {field.TypeName} newValue);",
                    _ => string.Empty
                };
                factory.SetteringBody.Add(intercept);
                factory.SetteringBody.Add($"OnPropertyChanging(nameof({field.PropertyName}));");
                factory.SetteredBody.Add($"OnPropertyChanged(nameof({field.PropertyName}));");
                sourceBuilder.AppendLine(factory.Generate());
                sourceBuilder.AppendLine(interceptmethod);
                sourceBuilder.AppendLine();
            }

            return sourceBuilder.ToString();
        }
    }
}
