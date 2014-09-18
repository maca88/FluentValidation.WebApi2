using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluentValidation.WebApi2
{
    using System.Web.Http;
    using System.Web.Http.Controllers;
    using System.Web.Http.ModelBinding;
    using Internal;

    public class CustomizeValidatorAttribute : Attribute
    {
        public string RuleSet { get; set; }
        public string Properties { get; set; }
        public Type Interceptor { get; set; }

        /// <summary>
        /// Builds a validator selector from the options specified in the attribute's properties.
        /// </summary>
        public IValidatorSelector ToValidatorSelector()
        {
            IValidatorSelector selector;

            if (!string.IsNullOrEmpty(RuleSet))
            {
                var rulesets = RuleSet.Split(',', ';');
                selector = new RulesetValidatorSelector(rulesets);
            }
            else if (!string.IsNullOrEmpty(Properties))
            {
                var properties = Properties.Split(',', ';');
                selector = new MemberNameValidatorSelector(properties);
            }
            else
            {
                selector = new DefaultValidatorSelector();
            }

            return selector;

        }

        public IValidatorInterceptor GetInterceptor()
        {
            if (Interceptor == null) return null;

            if (!typeof(IValidatorInterceptor).IsAssignableFrom(Interceptor))
            {
                throw new InvalidOperationException("Type {0} is not an IValidatorInterceptor. The Interceptor property of CustomizeValidatorAttribute must implement IValidatorInterceptor.");
            }

            var instance = Activator.CreateInstance(Interceptor) as IValidatorInterceptor;

            if (instance == null)
            {
                throw new InvalidOperationException("Type {0} is not an IValidatorInterceptor. The Interceptor property of CustomizeValidatorAttribute must implement IValidatorInterceptor.");
            }

            return instance;
        }

    }
}
