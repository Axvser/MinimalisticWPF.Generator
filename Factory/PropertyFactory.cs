using System.Collections.Generic;
using System.Text;

namespace MinimalisticWPF.Generator.Factory
{
    public class PropertyFactory(string modifies, string fullTypeName, string sourceName, string propertyName, bool isView = false) : IFactory
    {
        const string RETRACT = "      ";

        public string Modifies { get; private set; } = modifies;
        public string FullTypeName { get; private set; } = fullTypeName;
        public string SourceName { get; private set; } = sourceName;
        public string PropertyName { get; private set; } = propertyName;

        public List<string> SetterBody { get; set; } = [];
        public List<string> AttributeBody { get; set; } = [];

        private bool IsView { get; set; } = isView;

        public string GenerateViewModel()
        {
            var setterBody = new StringBuilder();
            for (int i = 0; i < SetterBody.Count; i++)
            {
                setterBody.AppendLine($"{RETRACT}       {SetterBody[i]}");
            }

            var attributeBody = new StringBuilder();
            for (int i = 0; i < AttributeBody.Count; i++)
            {
                if (i == AttributeBody.Count - 1)
                {
                    attributeBody.Append($"{RETRACT}[{AttributeBody[i]}]");
                }
                else
                {
                    attributeBody.AppendLine($"{RETRACT}[{AttributeBody[i]}]");
                }
            }

            return $$"""

                {{attributeBody}}
                {{RETRACT}}{{Modifies}} {{FullTypeName}} {{PropertyName}}
                {{RETRACT}}{
                {{RETRACT}}    get => {{SourceName}};
                {{RETRACT}}    set
                {{RETRACT}}    {
                {{RETRACT}}       var old = {{SourceName}};
                {{RETRACT}}       On{{PropertyName}}Changing(old,value);
                {{RETRACT}}       {{SourceName}} = value;               
                {{RETRACT}}       On{{PropertyName}}Changed(old,value);
                {{setterBody.ToString()}}
                {{RETRACT}}    }
                {{RETRACT}}}
                {{RETRACT}}
                {{RETRACT}}partial void On{{PropertyName}}Changing({{FullTypeName}} oldValue,{{FullTypeName}} newValue);
                {{RETRACT}}partial void On{{PropertyName}}Changed({{FullTypeName}} oldValue,{{FullTypeName}} newValue);
                """;
        }
        public string Generate()
        {
            return IsView ? GenerateProxy() : GenerateViewModel();
        }
        public string GenerateProxy()
        {
            var attributeBody = new StringBuilder();
            for (int i = 0; i < AttributeBody.Count; i++)
            {
                if (i == AttributeBody.Count - 1)
                {
                    attributeBody.Append($"{RETRACT}[{AttributeBody[i]}]");
                }
                else
                {
                    attributeBody.AppendLine($"{RETRACT}[{AttributeBody[i]}]");
                }
            }

            return $$"""

                {{attributeBody}}
                {{RETRACT}}{{Modifies}} {{FullTypeName}} {{PropertyName}}
                {{RETRACT}}{
                {{RETRACT}}    get => {{SourceName}};
                {{RETRACT}}    set => {{SourceName}} = value;
                {{RETRACT}}}
                """;
        }
    }
}
