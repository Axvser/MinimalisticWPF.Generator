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
        const string NAMESPACE_MVVM = "global::System.ComponentModel.";
        const string NAMESPACE_CONSTRUCTOR = "global::MinimalisticWPF.";
        const string NAMESPACE_TRANSITOIN = "global::MinimalisticWPF.TransitionSystem.";

        internal ViewModelRoslyn(ClassDeclarationSyntax classDeclarationSyntax, INamedTypeSymbol namedTypeSymbol, Compilation compilation) : base(classDeclarationSyntax, namedTypeSymbol, compilation)
        {
            IsViewModel = AnalizeHelper.IsViewModelClass(Symbol, out var vmfields);
            FieldRoslyns = vmfields.Select(field => new FieldRoslyn(field));
            ReadModelConfigParams(namedTypeSymbol, compilation);
        }

        public bool IsViewModel { get; set; } = false;

        public IEnumerable<FieldRoslyn> FieldRoslyns { get; set; } = [];

        public string ModelTypeName { get; set; } = string.Empty;
        public string ModelReaderName { get; set; } = string.Empty;
        public string[] ModelPropertyNames { get; set; } = [];
        private void ReadModelConfigParams(INamedTypeSymbol classSymbol, Compilation compilation)
        {
            var attributeData = classSymbol.GetAttributes()
                .FirstOrDefault(ad => ad.AttributeClass?.Name == "ModelConfigAttribute");
            if (attributeData != null)
            {
                ModelReaderName = (string)attributeData.ConstructorArguments[0].Value!;
                var validation = (string)attributeData.ConstructorArguments[1].Value!;
                var target = AnalizeHelper.FindTargetTypeSymbol(compilation, ModelReaderName, validation);

                if (target != null)
                {
                    ModelTypeName = target.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    ModelPropertyNames = target.GetMembers().OfType<IPropertySymbol>().Select(s => s.Name).ToArray();
                }
                else
                {
                    // 处理未找到的情况
                    throw new InvalidOperationException($"未找到类型：{ModelReaderName}（命名空间验证：{validation}）");
                }
            }
        }

        public string Generate()
        {
            var builder = new StringBuilder();

            builder.AppendLine(GenerateUsing());
            builder.AppendLine(GenerateNamespace());
            builder.AppendLine(GeneratePartialClass());
            builder.AppendLine(GenerateConstructor());
            builder.AppendLine(GenerateIPC());
            builder.AppendLine(GenerateITA());
            builder.AppendLine(GenerateInitialize());
            builder.AppendLine(GenerateModelReader());
            builder.AppendLine(GenerateHoverControl());
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
            }
            if (IsAop)
            {
                list.Add($"{NAMESPACE_AOP}{AnalizeHelper.GetInterfaceName(Syntax)}");
            }
            if (IsDynamicTheme)
            {
                list.Add($"{NAMESPACE_THEME}IThemeApplied");
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
        public string GenerateITA()
        {
            if (!IsDynamicTheme) return string.Empty;

            StringBuilder sourceBuilder = new();
            sourceBuilder.AppendLine("      public bool IsThemeChanging { get; set; } = false;");
            sourceBuilder.AppendLine("      public Type? CurrentTheme { get; set; } = null;");
            sourceBuilder.AppendLine("      public void RunThemeChanging(Type? oldTheme, Type newTheme)");
            sourceBuilder.AppendLine("      {");
            sourceBuilder.AppendLine("         OnThemeChanging(oldTheme ,newTheme);");
            sourceBuilder.AppendLine("      }");
            sourceBuilder.AppendLine("      public void RunThemeChanged(Type? oldTheme, Type newTheme)");
            sourceBuilder.AppendLine("      {");
            sourceBuilder.AppendLine("         OnThemeChanged(oldTheme ,newTheme);");
            if (FieldRoslyns.Any(f => f.CanHover))
            {
                sourceBuilder.AppendLine("         UpdateHoverState();");
            }
            sourceBuilder.AppendLine("      }");
            sourceBuilder.AppendLine("      partial void OnThemeChanging(Type? oldTheme, Type newTheme);");
            sourceBuilder.AppendLine("      partial void OnThemeChanged(Type? oldTheme, Type newTheme);");
            return sourceBuilder.ToString();
        }
        public string GenerateConstructor()
        {
            var acc = AnalizeHelper.GetAccessModifier(Symbol);

            var methods = Symbol.GetMembers()
                .OfType<IMethodSymbol>()
                .Where(m => m.GetAttributes().Any(att => att.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == $"{NAMESPACE_CONSTRUCTOR}ConstructorAttribute"))
                .ToList();

            StringBuilder builder = new();
            var strAop = $"{NAMESPACE_AOP}{AnalizeHelper.GetInterfaceName(Syntax)}";
            if (IsAop)
            {
                builder.AppendLine($$"""
                                           public {{strAop}} Proxy { get; private set; }
                                     """);
                builder.AppendLine();
            }

            builder.AppendLine($"      {acc} {Symbol.Name} ()");
            builder.AppendLine("      {");
            if (IsAop)
            {
                builder.AppendLine($"         Proxy = this.CreateProxy<{strAop}>();");
            }
            if (IsDynamicTheme)
            {
                builder.AppendLine($"         {NAMESPACE_CONSTRUCTOR}DynamicTheme.Awake(this);");
            }
            foreach (var method in methods.Where(m => !m.Parameters.Any()))
            {
                builder.AppendLine($"         {method.Name}();");
            }
            if (FieldRoslyns.Any(f => f.CanHover))
            {
                builder.AppendLine($$"""
                         HoveredTransition.TransitionParams.Start += () =>
                         {
                             IsHoverChanging = true;
                         };
                         HoveredTransition.TransitionParams.Completed += () =>
                         {
                             IsHoverChanging = false;
                         };
                         NoHoveredTransition.TransitionParams.Start += () =>
                         {
                             IsHoverChanging = true;
                         };
                         NoHoveredTransition.TransitionParams.Completed += () =>
                         {
                             IsHoverChanging = false;
                         };
                """);
            }

            builder.AppendLine("      }");

            var groupedMethods = methods.Where(m => m.Parameters.Any()).GroupBy(m =>
                string.Join(",", m.Parameters.Select(p => p.Type.ToDisplayString())));

            foreach (var group in groupedMethods)
            {
                var parameters = group.Key.Split(',');
                var parameterList = string.Join(", ", group.First().Parameters.Select(p => $"{p.Type.ToDisplayString()} {p.Name}"));
                var callParameters = string.Join(", ", group.First().Parameters.Select(p => p.Name));

                builder.AppendLine();
                builder.AppendLine($"      {acc} {Symbol.Name} ({parameterList})");
                builder.AppendLine("      {");
                if (IsAop)
                {
                    builder.AppendLine($"         Proxy = this.CreateProxy<{strAop}>();");
                }
                if (IsDynamicTheme)
                {
                    builder.AppendLine($"         {NAMESPACE_CONSTRUCTOR}DynamicTheme.Awake(this);");
                }
                foreach (var method in group)
                {
                    builder.AppendLine($"         {method.Name}({callParameters});");
                }
                if (FieldRoslyns.Any(f => f.CanHover))
                {
                    builder.AppendLine($$"""
                         HoveredTransition.TransitionParams.Start += () =>
                         {
                             IsHoverChanging = true;
                         };
                         HoveredTransition.TransitionParams.Completed += () =>
                         {
                             IsHoverChanging = false;
                         };
                         NoHoveredTransition.TransitionParams.Start += () =>
                         {
                             IsHoverChanging = true;
                         };
                         NoHoveredTransition.TransitionParams.Completed += () =>
                         {
                             IsHoverChanging = false;
                         };
                """);
                }
                builder.AppendLine("      }");
            }

            return builder.ToString();
        }
        public string GenerateIPC()
        {
            StringBuilder sourceBuilder = new();
            string source = $$"""
                                    public event {{NAMESPACE_MVVM}}PropertyChangedEventHandler? PropertyChanged;
                                    public void OnPropertyChanged(string propertyName)
                                    {
                                       PropertyChanged?.Invoke(this, new {{NAMESPACE_MVVM}}PropertyChangedEventArgs(propertyName));
                                    }
                              """;
            sourceBuilder.AppendLine(source);

            foreach (var field in FieldRoslyns)
            {
                var factory = new PropertyFactory("public", field.TypeName, field.FieldName, field.PropertyName);
                factory.AttributeBody = field.ThemeAttributes;
                factory.SetterBody.Add($"OnPropertyChanged(nameof({field.PropertyName}));");
                sourceBuilder.AppendLine(factory.Generate());
                sourceBuilder.AppendLine();
            }

            return sourceBuilder.ToString();
        }
        public string GenerateInitialize()
        {
            var sourceBuilder = new StringBuilder();

            foreach (var fieldRoslyn in FieldRoslyns)
            {
                sourceBuilder.AppendLine(fieldRoslyn.GenerateInitializeFunction(Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
            }

            return sourceBuilder.ToString();
        }
        public string GenerateHoverControl()
        {
            var hoverables = FieldRoslyns.Where(fr => fr.CanHover).ToArray();

            if (hoverables.Length < 1) return string.Empty;

            StringBuilder sourceBuilder = new();

            if (IsDynamicTheme)
            {
                sourceBuilder.AppendLine($$"""
                      private bool _isHovered = false;
                      public bool IsHovered
                      {
                         get => _isHovered;
                         set
                         {
                            if(_isHovered != value)
                            {
                               _isHovered = value;
                               if (!IsThemeChanging)
                               {
                                   this.BeginTransition(value ? HoveredTransition : NoHoveredTransition);
                               }
                            }
                         }
                      }
                """);
            }
            else
            {
                sourceBuilder.AppendLine($$"""
                      private bool _isHovered = false;
                      public bool IsHovered
                      {
                         get => _isHovered;
                         set
                         {
                            if(_isHovered != value)
                            {
                               _isHovered = value;
                               this.BeginTransition(value ? HoveredTransition : NoHoveredTransition);
                            }
                         }
                      }
                """);
            }

            sourceBuilder.AppendLine();

            sourceBuilder.AppendLine($$"""
                      private bool _isHoverChanging = false;
                      public bool IsHoverChanging
                      {
                         get => _isHoverChanging;
                         set
                         {
                            if(_isHoverChanging != value)
                            {
                               _isHoverChanging = value;
                            }
                         }
                      }
                """);

            sourceBuilder.AppendLine();

            sourceBuilder.AppendLine($$"""
                      public {{NAMESPACE_TRANSITOIN}}TransitionBoard<{{Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}}> HoveredTransition { get; set; } = {{NAMESPACE_TRANSITOIN}}Transition.Create<{{Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}}>();
                """);
            sourceBuilder.AppendLine($$"""
                      public {{NAMESPACE_TRANSITOIN}}TransitionBoard<{{Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}}> NoHoveredTransition { get; set; } = {{NAMESPACE_TRANSITOIN}}Transition.Create<{{Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}}>()
                """);
            for (var i = 0; i < hoverables.Length; i++)
            {
                if (!string.IsNullOrEmpty(hoverables[i].Initial))
                {
                    sourceBuilder.AppendLine($"         .SetProperty(x => x.{hoverables[i].PropertyName}, {hoverables[i].Initial.Replace("=", string.Empty).TrimStart()})");
                }
            }
            sourceBuilder.Append("         ;");

            sourceBuilder.AppendLine();

            // 不同主题下的悬停控制属性
            foreach (var fieldRoslyn in hoverables)
            {
                if (IsDynamicTheme && fieldRoslyn.ThemeAttributes.Count > 0)
                {
                    foreach (var (fullthemeText, themeText) in fieldRoslyn.ThemeAttributes.Select(t => (t.Split('(')[0], AnalizeHelper.ExtractThemeName(t))))
                    {
                        sourceBuilder.AppendLine($"      private {fieldRoslyn.TypeName} _{themeText}Hovered{fieldRoslyn.PropertyName} = ({fieldRoslyn.TypeName}){NAMESPACE_CONSTRUCTOR}DynamicTheme.GetSharedValue(typeof({Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}),typeof({fullthemeText}),\"{fieldRoslyn.PropertyName}\");");
                        sourceBuilder.AppendLine($"      public {fieldRoslyn.TypeName} {themeText}Hovered{fieldRoslyn.PropertyName}");
                        sourceBuilder.AppendLine("      {");
                        sourceBuilder.AppendLine($"         get => _{themeText}Hovered{fieldRoslyn.PropertyName};");
                        sourceBuilder.AppendLine("         set");
                        sourceBuilder.AppendLine("         {");
                        sourceBuilder.AppendLine($"            var oldValue = _{themeText}Hovered{fieldRoslyn.PropertyName};");
                        sourceBuilder.AppendLine($"            _{themeText}Hovered{fieldRoslyn.PropertyName} = value;");
                        sourceBuilder.AppendLine($$"""
                                        if(CurrentTheme == typeof({{fullthemeText}}))
                                        {
                                           HoveredTransition.SetProperty(x => x.{{fieldRoslyn.PropertyName}}, value);
                                           if(!IsHoverChanging && IsHovered && !IsThemeChanging)
                                           {
                                              {{fieldRoslyn.PropertyName}} = value;
                                           }
                                        }
                            """);
                        sourceBuilder.AppendLine($"            On{themeText}Hovered{fieldRoslyn.PropertyName}Changed(oldValue,value);");
                        sourceBuilder.AppendLine("         }");
                        sourceBuilder.AppendLine("      }");
                        sourceBuilder.AppendLine($"      partial void On{themeText}Hovered{fieldRoslyn.PropertyName}Changed({fieldRoslyn.TypeName} oldValue,{fieldRoslyn.TypeName} newValue);");
                        sourceBuilder.AppendLine();

                        sourceBuilder.AppendLine($"      private {fieldRoslyn.TypeName} _{themeText}NoHovered{fieldRoslyn.PropertyName} = ({fieldRoslyn.TypeName}){NAMESPACE_CONSTRUCTOR}DynamicTheme.GetSharedValue(typeof({Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}),typeof({fullthemeText}),\"{fieldRoslyn.PropertyName}\");");
                        sourceBuilder.AppendLine($"      public {fieldRoslyn.TypeName} {themeText}NoHovered{fieldRoslyn.PropertyName}");
                        sourceBuilder.AppendLine("      {");
                        sourceBuilder.AppendLine($"         get => _{themeText}NoHovered{fieldRoslyn.PropertyName};");
                        sourceBuilder.AppendLine("         set");
                        sourceBuilder.AppendLine("         {");
                        sourceBuilder.AppendLine($"            var oldValue = _{themeText}NoHovered{fieldRoslyn.PropertyName};");
                        sourceBuilder.AppendLine($"            _{themeText}NoHovered{fieldRoslyn.PropertyName} = value;");
                        sourceBuilder.AppendLine($$"""
                                        if(CurrentTheme == typeof({{fullthemeText}}))
                                        {
                                           NoHoveredTransition.SetProperty(x => x.{{fieldRoslyn.PropertyName}}, value);
                                           if(!IsHoverChanging && !IsHovered && !IsThemeChanging)
                                           {
                                              {{fieldRoslyn.PropertyName}} = value;
                                           }
                                        }
                            """);
                        sourceBuilder.AppendLine($"            On{themeText}NoHovered{fieldRoslyn.PropertyName}Changed(oldValue,value);");
                        if (fieldRoslyn.CanIsolated)
                        {
                            sourceBuilder.AppendLine($"            {NAMESPACE_CONSTRUCTOR}DynamicTheme.SetIsolatedValue(this,typeof({fullthemeText}),\"{fieldRoslyn.PropertyName}\",value);");
                        }
                        else
                        {
                            sourceBuilder.AppendLine($"            {NAMESPACE_CONSTRUCTOR}DynamicTheme.SetSharedValue(typeof({Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}),typeof({fullthemeText}),\"{fieldRoslyn.PropertyName}\",value);");
                        }
                        sourceBuilder.AppendLine("         }");
                        sourceBuilder.AppendLine("      }");
                        sourceBuilder.AppendLine($"      partial void On{themeText}NoHovered{fieldRoslyn.PropertyName}Changed({fieldRoslyn.TypeName} oldValue,{fieldRoslyn.TypeName} newValue);");
                        sourceBuilder.AppendLine();
                    }
                }
                else
                {
                    sourceBuilder.AppendLine($"      private {fieldRoslyn.TypeName} _Hovered{fieldRoslyn.PropertyName};");
                    sourceBuilder.AppendLine($"      public {fieldRoslyn.TypeName} Hovered{fieldRoslyn.PropertyName}");
                    sourceBuilder.AppendLine("      {");
                    sourceBuilder.AppendLine($"         get => _Hovered{fieldRoslyn.PropertyName};");
                    sourceBuilder.AppendLine("         set");
                    sourceBuilder.AppendLine("         {");
                    sourceBuilder.AppendLine($"            var oldValue = _Hovered{fieldRoslyn.PropertyName};");
                    sourceBuilder.AppendLine($"            _Hovered{fieldRoslyn.PropertyName} = value;");
                    sourceBuilder.AppendLine($$"""
                                    HoveredTransition.SetProperty(x => x.{{fieldRoslyn.PropertyName}}, value);
                        """);
                    sourceBuilder.AppendLine($"            OnHovered{fieldRoslyn.PropertyName}Changed(oldValue,value);");
                    sourceBuilder.AppendLine("         }");
                    sourceBuilder.AppendLine("      }");
                    sourceBuilder.AppendLine($"      partial void OnHovered{fieldRoslyn.PropertyName}Changed({fieldRoslyn.TypeName} oldValue,{fieldRoslyn.TypeName} newValue);");
                    sourceBuilder.AppendLine();

                    sourceBuilder.AppendLine($"      private {fieldRoslyn.TypeName} _NoHovered{fieldRoslyn.PropertyName}{fieldRoslyn.Initial};");
                    sourceBuilder.AppendLine($"      public {fieldRoslyn.TypeName} NoHovered{fieldRoslyn.PropertyName}");
                    sourceBuilder.AppendLine("      {");
                    sourceBuilder.AppendLine($"         get => _NoHovered{fieldRoslyn.PropertyName};");
                    sourceBuilder.AppendLine("         set");
                    sourceBuilder.AppendLine("         {");
                    sourceBuilder.AppendLine($"            var oldValue = _NoHovered{fieldRoslyn.PropertyName};");
                    sourceBuilder.AppendLine($"            _NoHovered{fieldRoslyn.PropertyName} = value;");
                    sourceBuilder.AppendLine($$"""
                                    NoHoveredTransition.SetProperty(x => x.{{fieldRoslyn.PropertyName}}, value);
                                    if (!IsHoverChanging && !IsHovered)
                                    {
                                        {{fieldRoslyn.PropertyName}} = value;
                                    }
                        """);
                    sourceBuilder.AppendLine($"            OnNoHovered{fieldRoslyn.PropertyName}Changed(oldValue,value);");
                    sourceBuilder.AppendLine("         }");
                    sourceBuilder.AppendLine("      }");
                    sourceBuilder.AppendLine($"      partial void OnNoHovered{fieldRoslyn.PropertyName}Changed({fieldRoslyn.TypeName} oldValue,{fieldRoslyn.TypeName} newValue);");
                    sourceBuilder.AppendLine();
                }
            }

            sourceBuilder.AppendLine();

            //生成主题修改后的动画效果更新函数
            sourceBuilder.AppendLine("      protected virtual void UpdateHoverState()");
            sourceBuilder.AppendLine("      {");
            if (IsDynamicTheme)
            {
                sourceBuilder.AppendLine("         if(CurrentTheme != null)");
                sourceBuilder.AppendLine("         {");
                foreach (var fieldRoslyn in hoverables)
                {
                    if (IsDynamicTheme && fieldRoslyn.ThemeAttributes.Count > 0)
                    {
                        sourceBuilder.AppendLine($"             HoveredTransition.SetProperty(b => b.{fieldRoslyn.PropertyName}, {fieldRoslyn.PropertyName}_SelectThemeValue_Hovered(CurrentTheme.Name));");
                        sourceBuilder.AppendLine($"             NoHoveredTransition.SetProperty(b => b.{fieldRoslyn.PropertyName}, {fieldRoslyn.PropertyName}_SelectThemeValue_NoHovered(CurrentTheme.Name));");
                    }
                }
                sourceBuilder.AppendLine("         }");
            }
            sourceBuilder.AppendLine($"          this.BeginTransition(IsHovered ? HoveredTransition : NoHoveredTransition);");
            sourceBuilder.AppendLine("      }");

            //生成Hovered值选择器
            foreach (var fieldRoslyn in hoverables)
            {
                if (IsDynamicTheme && fieldRoslyn.ThemeAttributes.Count > 0)
                {
                    sourceBuilder.AppendLine($"      protected {fieldRoslyn.TypeName} {fieldRoslyn.PropertyName}_SelectThemeValue_Hovered(string themeName)");
                    sourceBuilder.AppendLine("      {");
                    sourceBuilder.AppendLine($"         switch(themeName)");
                    sourceBuilder.AppendLine("         {");
                    foreach (var themeText in fieldRoslyn.ThemeAttributes.Select(t => AnalizeHelper.ExtractThemeName(t)))
                    {
                        sourceBuilder.AppendLine($"            case \"{themeText}\":");
                        sourceBuilder.AppendLine($"                 return {themeText}Hovered{fieldRoslyn.PropertyName};");
                    }
                    sourceBuilder.AppendLine("         }");
                    sourceBuilder.AppendLine($"         return {fieldRoslyn.PropertyName};");
                    sourceBuilder.AppendLine("      }");
                }
                sourceBuilder.AppendLine();
            }

            //生成NoHovered值选择器
            foreach (var fieldRoslyn in hoverables)
            {
                if (IsDynamicTheme && fieldRoslyn.ThemeAttributes.Count > 0)
                {
                    sourceBuilder.AppendLine($"      protected {fieldRoslyn.TypeName} {fieldRoslyn.PropertyName}_SelectThemeValue_NoHovered(string themeName)");
                    sourceBuilder.AppendLine("      {");
                    sourceBuilder.AppendLine($"         switch(themeName)");
                    sourceBuilder.AppendLine("         {");
                    foreach (var themeText in fieldRoslyn.ThemeAttributes.Select(t => AnalizeHelper.ExtractThemeName(t)))
                    {
                        sourceBuilder.AppendLine($"            case \"{themeText}\":");
                        sourceBuilder.AppendLine($"                 return {themeText}NoHovered{fieldRoslyn.PropertyName};");
                    }
                    sourceBuilder.AppendLine("         }");
                    sourceBuilder.AppendLine($"         return {fieldRoslyn.PropertyName};");
                    sourceBuilder.AppendLine("      }");
                }
                sourceBuilder.AppendLine();
            }

            return sourceBuilder.ToString();
        }
        public string GenerateModelReader()
        {
            if (string.IsNullOrEmpty(ModelTypeName) || string.IsNullOrEmpty(ModelReaderName)) return string.Empty;

            var sourceBuilder = new StringBuilder();
            sourceBuilder.AppendLine($"      public {ModelTypeName} To{ModelReaderName}()");
            sourceBuilder.AppendLine("      {");
            sourceBuilder.AppendLine($"        var model = new {ModelTypeName}();");
            foreach (var field in FieldRoslyns)
            {
                var targetProperty = string.IsNullOrEmpty(field.ModelAlias)
                    ? field.PropertyName
                    : field.ModelAlias;

                if (ModelPropertyNames.Contains(targetProperty))
                {
                    sourceBuilder.AppendLine($"        model.{targetProperty} = this.{field.PropertyName};");
                }
            }
            sourceBuilder.AppendLine("        return model;");
            sourceBuilder.AppendLine("      }");
            return sourceBuilder.ToString();
        }
    }
}
