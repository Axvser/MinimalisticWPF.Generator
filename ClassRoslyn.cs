﻿using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MinimalisticWPF.Generator
{
    internal class ClassRoslyn
    {
        internal ClassRoslyn(ClassDeclarationSyntax classDeclarationSyntax, INamedTypeSymbol namedTypeSymbol, Compilation compilation)
        {
            Syntax = classDeclarationSyntax;
            Symbol = namedTypeSymbol;
            Compilation = compilation;
            IsDynamicTheme = namedTypeSymbol
                .GetMembers()
                .Any(member => member.GetAttributes()
                .Any(att => att.AttributeClass?.AllInterfaces.Any(i => i.Name == "IThemeAttribute") ?? false));
            IsThemeAttributeExsist = namedTypeSymbol.GetAttributes()
                .Any(att => att.AttributeClass?.Name == "DynamicThemeAttribute");
            IsAop = AnalizeHelper.IsAopClass(classDeclarationSyntax);
            IsViewModel = IsObservableFieldExist(namedTypeSymbol, out var vmfields);
            if (vmfields != null)
            {
                FieldRoslyns = vmfields.Select(field => new FieldRoslyn(field));
                IsHoverApplied = FieldRoslyns.Any(f => f.CanHover);
            }
            ReadContextConfigParams(namedTypeSymbol);
            ReadModelConfigParams(namedTypeSymbol, compilation);
        }

        public ClassDeclarationSyntax Syntax { get; private set; }
        public INamedTypeSymbol Symbol { get; private set; }
        public Compilation Compilation { get; private set; }
        public bool IsAop { get; private set; } = false;
        public bool IsDynamicTheme { get; private set; } = false;
        public bool IsViewModel { get; private set; } = false;
        public bool IsHoverApplied { get; private set; } = false;
        public bool IsThemeAttributeExsist { get; private set; } = false;
        public IEnumerable<FieldRoslyn> FieldRoslyns { get; private set; } = [];

        public bool IsContextConfig { get; private set; } = false;
        public string ViewModelTypeName { get; private set; } = string.Empty;
        public string ViewModelValidation { get; private set; } = string.Empty;

        public string ModelTypeName { get; private set; } = string.Empty;
        public string ModelReaderName { get; private set; } = string.Empty;
        public string[] ModelPropertyNames { get; private set; } = [];

        private static bool FindControlBase(INamedTypeSymbol typeSymbol)
        {
            var localBaseType = typeSymbol.BaseType;
            if (localBaseType != null)
            {
                if (typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "System.Windows.Controls.Control")
                {
                    return true;
                }
                else
                {
                    if (localBaseType.BaseType != null)
                    {
                        return FindControlBase(localBaseType.BaseType);
                    }
                    else
                    {
                        return false;
                    }
                }
            }
            else
            {
                return false;
            }
        }
        private static bool IsObservableFieldExist(INamedTypeSymbol classSymbol, out IEnumerable<IFieldSymbol> fieldSymbols)
        {
            fieldSymbols = classSymbol.GetMembers()
                    .OfType<IFieldSymbol>()
                    .Where(field => field.GetAttributes().Any(attr => attr.AttributeClass?.Name == "ObservableAttribute"));
            return fieldSymbols.Any();
        }
        public static HashSet<string> GetReferencedNamespaces(INamedTypeSymbol namedTypeSymbol)
        {
            HashSet<string> namespaces = new();

            var syntaxRef = namedTypeSymbol.DeclaringSyntaxReferences.FirstOrDefault();
            if (syntaxRef == null)
            {
                return namespaces;
            }
            if (syntaxRef.GetSyntax() is not ClassDeclarationSyntax classDecl)
            {
                return namespaces;
            }
            var compilationUnit = classDecl.FirstAncestorOrSelf<CompilationUnitSyntax>();
            if (compilationUnit == null)
            {
                return namespaces;
            }

            foreach (var usingDirective in compilationUnit.Usings)
            {
                if (usingDirective != null)
                {
                    // 处理普通 using 指令
                    if (usingDirective.Name != null)
                    {
                        // 使用 ToFullString 获取完整的命名空间
                        namespaces.Add($"using {usingDirective.Name.ToFullString()};");
                    }
                    // 处理别名 using 指令
                    else if (usingDirective.Alias != null)
                    {
                        // 例如：using Media = System.Windows.Media;
                        namespaces.Add($"using {usingDirective.Alias.Name} = {usingDirective.Name?.ToFullString()};");
                    }
                }
            }

            return namespaces;
        }
        private void ReadContextConfigParams(INamedTypeSymbol fieldSymbol)
        {
            var attributeData = fieldSymbol.GetAttributes()
                .FirstOrDefault(ad => ad.AttributeClass?.Name == "DataContextConfigAttribute");
            if (attributeData == null)
            {
                IsContextConfig = false;
                return;
            }
            IsContextConfig = true;
            ViewModelTypeName = (string)attributeData.ConstructorArguments[0].Value!;
            ViewModelValidation = (string)attributeData.ConstructorArguments[1].Value!;
        }
        private void ReadModelConfigParams(INamedTypeSymbol classSymbol, Compilation compilation)
        {
            var attributeData = classSymbol.GetAttributes()
                .FirstOrDefault(ad => ad.AttributeClass?.Name == "ModelConfigAttribute");
            if (attributeData != null)
            {
                ModelReaderName = (string)attributeData.ConstructorArguments[0].Value!;
                var validation = (string)attributeData.ConstructorArguments[1].Value!;
                var target = FindTargetModel(compilation, ModelReaderName, validation);

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
        private static INamedTypeSymbol? FindTargetModel(
            Compilation compilation,
            string typeName,
            string namespaceValidation)
        {
            if (string.IsNullOrEmpty(namespaceValidation))
            {
                // 查找所有同名类型并返回第一个
                return compilation.GetSymbolsWithName(typeName, SymbolFilter.Type)
                    .OfType<INamedTypeSymbol>()
                    .FirstOrDefault();
            }
            else
            {
                // 构造全名并精确查找
                var fullName = $"{namespaceValidation}.{typeName}";
                return compilation.GetTypeByMetadataName(fullName) as INamedTypeSymbol;
            }
        }

        public string GenerateUsing(HashSet<string> others)
        {
            StringBuilder sourceBuilder = new();
            sourceBuilder.AppendLine("#nullable enable");
            sourceBuilder.AppendLine();
            var hashUsings = GetReferencedNamespaces(Symbol);
            hashUsings.Add("using MinimalisticWPF;");
            hashUsings.Add("using MinimalisticWPF.Theme;");
            hashUsings.Add("using MinimalisticWPF.TransitionSystem;");
            if (IsViewModel)
            {
                hashUsings.Add("using System.ComponentModel;");
            }
            if (IsDynamicTheme)
            {
                hashUsings.Add("using MinimalisticWPF.StructuralDesign.Theme;");
            }
            if (IsAop)
            {
                hashUsings.Add("using MinimalisticWPF.AopInterfaces;");
            }
            if (IsContextConfig)
            {
                hashUsings.Add("using System.Windows;");
            }
            foreach (var name in others)
            {
                hashUsings.Add(name);
            }
            foreach (var use in hashUsings)
            {
                sourceBuilder.AppendLine(use);
            }
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
        public string GeneratePartialClass(bool ignoreViewModel = false, bool ignoreTheme = false)
        {
            StringBuilder sourceBuilder = new();
            string share = $"{Syntax.Modifiers} class {Syntax.Identifier.Text}";

            var list = new List<string>();
            if (IsAop)
            {
                list.Add(AnalizeHelper.GetInterfaceName(Syntax));
            }
            if (IsViewModel && !ignoreViewModel)
            {
                list.Add("INotifyPropertyChanged");
            }
            if (IsDynamicTheme && !ignoreTheme)
            {
                list.Add("IThemeApplied");
            }
            if (list.Count > 0)
            {
                var result = string.Join(", ", list);
                var source = $$"""
                              {{share}} : {{result}}
                              {
                           """;
                if (IsDynamicTheme && !IsThemeAttributeExsist && !ignoreTheme)
                {
                    sourceBuilder.AppendLine("   [DynamicTheme]");
                }
                sourceBuilder.AppendLine(source);
            }
            else
            {
                var source = $$"""
                              {{share}}
                              {
                           """;
                if (IsDynamicTheme && !IsThemeAttributeExsist && !ignoreTheme)
                {
                    sourceBuilder.AppendLine("   [DynamicTheme]");
                }
                sourceBuilder.AppendLine(source);
            }

            return sourceBuilder.ToString();
        }
        public string GenerateConstructor()
        {
            if (IsContextConfig)
            {
                return string.Empty;
            }

            var acc = AnalizeHelper.GetAccessModifier(Symbol);

            var methods = Symbol.GetMembers()
                .OfType<IMethodSymbol>()
                .Where(m => m.GetAttributes().Any(att => att.AttributeClass?.Name == "ConstructorAttribute"))
                .ToList();

            StringBuilder builder = new();
            var strAop = $"IAop{Symbol.Name}In{Symbol.ContainingNamespace?.ToString()?.Replace('.', '_')}";
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
                builder.AppendLine($"         DynamicTheme.Awake(this);");
            }
            foreach (var method in methods.Where(m => !m.Parameters.Any()))
            {
                builder.AppendLine($"         {method.Name}();");
            }
            if (IsHoverApplied)
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
                    builder.AppendLine($"         DynamicTheme.Awake(this);");
                }
                foreach (var method in group)
                {
                    builder.AppendLine($"         {method.Name}({callParameters});");
                }
                if (IsHoverApplied)
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
            if (IsContextConfig)
            {
                return string.Empty;
            }
            if (!IsViewModel) return string.Empty;
            StringBuilder sourceBuilder = new();
            string source = $$"""
                                    public event PropertyChangedEventHandler? PropertyChanged;
                                    public void OnPropertyChanged(string propertyName)
                                    {
                                       PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
                                    }
                              """;
            sourceBuilder.AppendLine(source);
            return sourceBuilder.ToString();
        }
        public string GenerateITA()
        {
            if (IsContextConfig)
            {
                return string.Empty;
            }
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
            sourceBuilder.AppendLine("      }");
            sourceBuilder.AppendLine("      partial void OnThemeChanging(Type? oldTheme, Type newTheme);");
            sourceBuilder.AppendLine("      partial void OnThemeChanged(Type? oldTheme, Type newTheme);");
            return sourceBuilder.ToString();
        }
        public string GenerateModelReader()
        {
            if (IsContextConfig || string.IsNullOrEmpty(ModelTypeName))
            {
                return string.Empty;
            }
            else
            {
                var sourceBuilder = new StringBuilder();
                // 生成 ToModel 方法
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
        public string GenerateModelReader(ClassRoslyn viewModel)
        {
            if (string.IsNullOrWhiteSpace(viewModel.ModelReaderName)) return string.Empty;

            var sourceBuilder = new StringBuilder();
            // 生成 ToModel 方法
            sourceBuilder.AppendLine($"      public {viewModel.ModelTypeName} To{viewModel.ModelReaderName}()");
            sourceBuilder.AppendLine("      {");
            sourceBuilder.AppendLine($"         return (({viewModel.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)})DataContext).To{viewModel.ModelReaderName}();");
            sourceBuilder.AppendLine("      }");
            return sourceBuilder.ToString();
        }
        public string GenerateHoverControl()
        {
            if (IsContextConfig)
            {
                return string.Empty;
            }

            var hoverables = FieldRoslyns.Where(fr => fr.CanHover).ToArray();

            if (hoverables.Length == 0) return string.Empty;

            StringBuilder sourceBuilder = new();

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
                      public TransitionBoard<{{Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}}> HoveredTransition { get; set; } = Transition.Create<{{Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}}>();
                """);
            sourceBuilder.AppendLine($$"""
                      public TransitionBoard<{{Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}}> NoHoveredTransition { get; set; } = Transition.Create<{{Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}}>()
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
                        sourceBuilder.AppendLine($"      private {fieldRoslyn.TypeName} _{themeText}Hovered{fieldRoslyn.PropertyName} = ({fieldRoslyn.TypeName})DynamicTheme.GetSharedValue(typeof({Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}),typeof({fullthemeText}),\"{fieldRoslyn.PropertyName}\");");
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

                        sourceBuilder.AppendLine($"      private {fieldRoslyn.TypeName} _{themeText}NoHovered{fieldRoslyn.PropertyName} = ({fieldRoslyn.TypeName})DynamicTheme.GetSharedValue(typeof({Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}),typeof({fullthemeText}),\"{fieldRoslyn.PropertyName}\");");
                        sourceBuilder.AppendLine($"      public {fieldRoslyn.TypeName} {themeText}NoHovered{fieldRoslyn.PropertyName}");
                        sourceBuilder.AppendLine("      {");
                        sourceBuilder.AppendLine($"         get => _{themeText}NoHovered{fieldRoslyn.PropertyName};");
                        sourceBuilder.AppendLine("         set");
                        sourceBuilder.AppendLine("         {");
                        sourceBuilder.AppendLine($"            var oldValue = _{themeText}NoHovered{fieldRoslyn.PropertyName};");
                        sourceBuilder.AppendLine($"            _{themeText}NoHovered{fieldRoslyn.PropertyName} = value;");
                        sourceBuilder.AppendLine($$"""
                                        if(CurrentTheme == typeof({{themeText}}))
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
                            sourceBuilder.AppendLine($"            DynamicTheme.SetIsolatedValue(this,typeof({fullthemeText}),\"{fieldRoslyn.PropertyName}\",value);");
                        }
                        else
                        {
                            sourceBuilder.AppendLine($"            DynamicTheme.SetSharedValue(typeof({Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}),typeof({fullthemeText}),\"{fieldRoslyn.PropertyName}\",value);");
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
            sourceBuilder.AppendLine("      protected virtual void UpdateState()");
            sourceBuilder.AppendLine("      {");
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
            sourceBuilder.AppendLine();
            sourceBuilder.AppendLine("          this.BeginTransition(IsHovered ? HoveredTransition : NoHoveredTransition);");
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
        public string GenerateInitializeInView(string vmTypeName)
        {
            var sourceBuilder = new StringBuilder();

            foreach (var fieldRoslyn in FieldRoslyns.Where(f => f.CanDependency))
            {
                sourceBuilder.AppendLine(fieldRoslyn.GenerateInitializeFunction(vmTypeName));
            }

            return sourceBuilder.ToString();
        }
        public string GenerateHoverDependencyProperties(string localTypeName, string vmTypeName)
        {
            var hoverables = FieldRoslyns.Where(fr => fr.CanHover && fr.CanDependency).ToArray();

            var sourceBuilder = new StringBuilder();

            foreach (var fieldRoslyn in hoverables)
            {
                if (IsDynamicTheme && fieldRoslyn.ThemeAttributes.Count > 0)
                {
                    foreach (var themeText in fieldRoslyn.ThemeAttributes.Select(t => AnalizeHelper.ExtractThemeName(t)))
                    {
                        sourceBuilder.AppendLine($$"""
                                   public {{fieldRoslyn.TypeName}} {{themeText}}Hovered{{fieldRoslyn.PropertyName}}
                                   {
                                      get => ({{fieldRoslyn.TypeName}})GetValue({{themeText}}Hovered{{fieldRoslyn.PropertyName}}Property);
                                      set => SetValue({{themeText}}Hovered{{fieldRoslyn.PropertyName}}Property, value);
                                   }
                                   public static readonly DependencyProperty {{themeText}}Hovered{{fieldRoslyn.PropertyName}}Property =
                                      DependencyProperty.Register(
                                      nameof({{themeText}}Hovered{{fieldRoslyn.PropertyName}}),
                                      typeof({{fieldRoslyn.TypeName}}),
                                      typeof({{localTypeName}}),
                                      new PropertyMetadata({{fieldRoslyn.Initial.InitialTextParse()}}, _innerRun{{themeText}}Hovered{{fieldRoslyn.PropertyName}}Changed));   
                                   public static void _innerRun{{themeText}}Hovered{{fieldRoslyn.PropertyName}}Changed(DependencyObject d, DependencyPropertyChangedEventArgs e)
                                   {
                                      if (d is {{localTypeName}} control && control.DataContext is {{vmTypeName}} viewModel)
                                      {
                                         viewModel.{{themeText}}Hovered{{fieldRoslyn.PropertyName}} = ({{fieldRoslyn.TypeName}})e.NewValue;
                                      }
                                   }
                            """);
                        sourceBuilder.AppendLine($$"""
                                   public {{fieldRoslyn.TypeName}} {{themeText}}NoHovered{{fieldRoslyn.PropertyName}}
                                   {
                                      get => ({{fieldRoslyn.TypeName}})GetValue({{themeText}}NoHovered{{fieldRoslyn.PropertyName}}Property);
                                      set => SetValue({{themeText}}NoHovered{{fieldRoslyn.PropertyName}}Property, value);
                                   }
                                   public static readonly DependencyProperty {{themeText}}NoHovered{{fieldRoslyn.PropertyName}}Property =
                                      DependencyProperty.Register(
                                      nameof({{themeText}}NoHovered{{fieldRoslyn.PropertyName}}),
                                      typeof({{fieldRoslyn.TypeName}}),
                                      typeof({{localTypeName}}),
                                      new PropertyMetadata({{fieldRoslyn.Initial.InitialTextParse()}}, _innerRun{{themeText}}NoHovered{{fieldRoslyn.PropertyName}}Changed));   
                                   public static void _innerRun{{themeText}}NoHovered{{fieldRoslyn.PropertyName}}Changed(DependencyObject d, DependencyPropertyChangedEventArgs e)
                                   {
                                      if (d is {{localTypeName}} control && control.DataContext is {{vmTypeName}} viewModel)
                                      {
                                         viewModel.{{themeText}}NoHovered{{fieldRoslyn.PropertyName}} = ({{fieldRoslyn.TypeName}})e.NewValue;
                                      }
                                   }
                            """);
                    }
                }
                else
                {
                    sourceBuilder.AppendLine($$"""
                                   public {{fieldRoslyn.TypeName}} Hovered{{fieldRoslyn.PropertyName}}
                                   {
                                      get => ({{fieldRoslyn.TypeName}})GetValue(Hovered{{fieldRoslyn.PropertyName}}Property);
                                      set => SetValue(Hovered{{fieldRoslyn.PropertyName}}Property, value);
                                   }
                                   public static readonly DependencyProperty Hovered{{fieldRoslyn.PropertyName}}Property =
                                      DependencyProperty.Register(
                                      nameof(Hovered{{fieldRoslyn.PropertyName}}),
                                      typeof({{fieldRoslyn.TypeName}}),
                                      typeof({{localTypeName}}),
                                      new PropertyMetadata({{fieldRoslyn.Initial.InitialTextParse()}}, _innerRunHovered{{fieldRoslyn.PropertyName}}Changed));   
                                   private static void _innerRunHovered{{fieldRoslyn.PropertyName}}Changed(DependencyObject d, DependencyPropertyChangedEventArgs e)
                                   {
                                      if (d is {{localTypeName}} control && control.DataContext is {{vmTypeName}} viewModel)
                                      {
                                         viewModel.Hovered{{fieldRoslyn.PropertyName}} = ({{fieldRoslyn.TypeName}})e.NewValue;
                                      }
                                   }
                            """);
                    sourceBuilder.AppendLine($$"""
                                   public {{fieldRoslyn.TypeName}} NoHovered{{fieldRoslyn.PropertyName}}
                                   {
                                      get => ({{fieldRoslyn.TypeName}})GetValue(Hovered{{fieldRoslyn.PropertyName}}Property);
                                      set => SetValue(NoHovered{{fieldRoslyn.PropertyName}}Property, value);
                                   }
                                   public static readonly DependencyProperty NoHovered{{fieldRoslyn.PropertyName}}Property =
                                      DependencyProperty.Register(
                                      nameof(NoHovered{{fieldRoslyn.PropertyName}}),
                                      typeof({{fieldRoslyn.TypeName}}),
                                      typeof({{localTypeName}}),
                                      new PropertyMetadata({{fieldRoslyn.Initial.InitialTextParse()}}, _innerRunNoHovered{{fieldRoslyn.PropertyName}}Changed));   
                                   private static void _innerRunNoHovered{{fieldRoslyn.PropertyName}}Changed(DependencyObject d, DependencyPropertyChangedEventArgs e)
                                   {
                                      if (d is {{localTypeName}} control && control.DataContext is {{vmTypeName}} viewModel)
                                      {
                                         viewModel.NoHovered{{fieldRoslyn.PropertyName}} = ({{fieldRoslyn.TypeName}})e.NewValue;
                                      }
                                   }
                            """);
                }
                sourceBuilder.AppendLine();
            }



            return sourceBuilder.ToString();
        }
        public string GenerateDependencyProperties(string localTypeName, string vmTypeName)
        {
            var dependencies = FieldRoslyns.Where(fr => fr.CanDependency).ToArray();

            var sourceBuilder = new StringBuilder();

            foreach (var fieldRoslyn in dependencies)
            {
                if (fieldRoslyn.ThemeAttributes.Count > 0)
                {
                    foreach (var (fullattName, attName) in fieldRoslyn.ThemeAttributes.Select(t => (t.Split('(')[0], AnalizeHelper.ExtractThemeName(t))))
                    {
                        sourceBuilder.AppendLine($$"""
                                   public {{fieldRoslyn.TypeName}} {{attName}}{{fieldRoslyn.PropertyName}}
                                   {
                                      get => ({{fieldRoslyn.TypeName}})GetValue({{attName}}{{fieldRoslyn.PropertyName}}Property);
                                      set => SetValue({{attName}}{{fieldRoslyn.PropertyName}}Property, value);
                                   }
                                   public static readonly DependencyProperty {{attName}}{{fieldRoslyn.PropertyName}}Property =
                                      DependencyProperty.Register(
                                      nameof({{attName}}{{fieldRoslyn.PropertyName}}),
                                      typeof({{fieldRoslyn.TypeName}}),
                                      typeof({{localTypeName}}),
                                      new PropertyMetadata({{fieldRoslyn.Initial.InitialTextParse()}}, _innerRun{{attName}}{{fieldRoslyn.PropertyName}}Changed));   
                                   public static void _innerRun{{attName}}{{fieldRoslyn.PropertyName}}Changed(DependencyObject d, DependencyPropertyChangedEventArgs e)
                                   {
                                      if (d is {{localTypeName}} control && control.DataContext is {{vmTypeName}} viewModel)
                                      {
                                         if(viewModel is MinimalisticWPF.StructuralDesign.Theme.IThemeApplied theme)
                                         {
                                            {{(fieldRoslyn.CanIsolated ? $"DynamicTheme.SetIsolatedValue(theme,typeof({fullattName}),\"{fieldRoslyn.PropertyName}\",e.NewValue);" : $"DynamicTheme.SetSharedValue(typeof({Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}),typeof({fullattName}),\"{fieldRoslyn.PropertyName}\",e.NewValue);")}}
                                            if(theme.CurrentTheme?.Name == "{{attName}}")
                                            {
                                               viewModel.{{fieldRoslyn.PropertyName}} = ({{fieldRoslyn.TypeName}})e.NewValue;
                                            }            
                                         }
                                         control._inner{{attName}}{{fieldRoslyn.PropertyName}}Changed(({{fieldRoslyn.TypeName}})e.OldValue ,({{fieldRoslyn.TypeName}})e.NewValue);
                                      }
                                   }
                                   public void _inner{{attName}}{{fieldRoslyn.PropertyName}}Changed({{fieldRoslyn.TypeName}} oldValue, {{fieldRoslyn.TypeName}} newValue)
                                   {
                                      On{{attName}}{{fieldRoslyn.PropertyName}}Changed(oldValue ,newValue);
                                   }
                                   partial void On{{attName}}{{fieldRoslyn.PropertyName}}Changed({{fieldRoslyn.TypeName}} oldValue, {{fieldRoslyn.TypeName}} newValue);
                            """);
                    }
                }
                sourceBuilder.AppendLine($$"""
                                   public {{fieldRoslyn.TypeName}} {{fieldRoslyn.PropertyName}}
                                   {
                                      get => ({{fieldRoslyn.TypeName}})GetValue({{fieldRoslyn.PropertyName}}Property);
                                      set => SetValue({{fieldRoslyn.PropertyName}}Property, value);
                                   }
                                   public static readonly DependencyProperty {{fieldRoslyn.PropertyName}}Property =
                                      DependencyProperty.Register(
                                      nameof({{fieldRoslyn.PropertyName}}),
                                      typeof({{fieldRoslyn.TypeName}}),
                                      typeof({{localTypeName}}),
                                      new PropertyMetadata({{fieldRoslyn.Initial.InitialTextParse()}}, _innerRun{{fieldRoslyn.PropertyName}}Changed));   
                                   public static void _innerRun{{fieldRoslyn.PropertyName}}Changed(DependencyObject d, DependencyPropertyChangedEventArgs e)
                                   {
                                      if (d is {{localTypeName}} control && control.DataContext is {{vmTypeName}} viewModel)
                                      {
                                         viewModel.{{fieldRoslyn.PropertyName}} = ({{fieldRoslyn.TypeName}})e.NewValue;
                                         control._inner{{fieldRoslyn.PropertyName}}Changed(({{fieldRoslyn.TypeName}})e.OldValue ,({{fieldRoslyn.TypeName}})e.NewValue);
                                      }
                                   }
                                   public void _inner{{fieldRoslyn.PropertyName}}Changed({{fieldRoslyn.TypeName}} oldValue, {{fieldRoslyn.TypeName}} newValue)
                                   {
                                      On{{fieldRoslyn.PropertyName}}Changed(oldValue ,newValue);
                                   }
                                   partial void On{{fieldRoslyn.PropertyName}}Changed({{fieldRoslyn.TypeName}} oldValue, {{fieldRoslyn.TypeName}} newValue);
                            """);
            }

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
