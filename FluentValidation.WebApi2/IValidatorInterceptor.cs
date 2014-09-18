using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluentValidation.WebApi2
{
    using System.Web.Http.Controllers;
    using System.Web.Http.Metadata;
    using Results;

    /// <summary>
    /// Specifies an interceptor that can be used to provide hooks that will be called before and after MVC validation occurs.
    /// </summary>
    public interface IValidatorInterceptor
    {
        /// <summary>
        /// Invoked before WebApi validation takes place which allows the ValidationContext to be customized prior to validation.
        /// It should return a ValidationContext object.
        /// </summary>
        /// <param name="actionContext">Action context</param>
        /// <param name="metadata">Model metadata</param>
        /// <param name="validationContext">Validation Context</param>
        /// <returns>Validation Context</returns>
        ValidationContext BeforeWebApiValidation(HttpActionContext actionContext, ModelMetadata metadata, ValidationContext validationContext);

        /// <summary>
        /// Invoked after WebApi validation takes place which allows the result to be customized.
        /// It should return a ValidationResult.
        /// </summary>
        /// <param name="actionContext">Action context</param>
        /// <param name="metadata">Model metadata</param>
        /// <param name="validationContext">Validation Context</param>
        /// <param name="result">The result of validation.</param>
        /// <returns>Validation Context</returns>
        ValidationResult AfterWebApiValidation(HttpActionContext actionContext, ModelMetadata metadata, ValidationContext validationContext, ValidationResult result);
    }
}
