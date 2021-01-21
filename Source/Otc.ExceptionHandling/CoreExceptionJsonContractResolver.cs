using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Otc.ExceptionHandling
{

    /// <summary>
    /// Ignora as propriedades que sao da classe Exception, exceto Message.
    /// </summary>
    internal class CoreExceptionJsonContractResolver : CamelCasePropertyNamesContractResolver
    {
        private static readonly HashSet<string> IgnoreProperties = new HashSet<string>(
            typeof(Exception).GetProperties().Where(p => p.Name != nameof(Exception.Message))
                .Select(p => p.Name)
        );

        protected override JsonProperty CreateProperty(MemberInfo member,
            MemberSerialization memberSerialization)
        {
            JsonProperty property = base.CreateProperty(member, memberSerialization);

            if (IgnoreProperties.Contains(member.Name))
            {
                property.Ignored = true;
            }

            return property;
        }
    }
}
