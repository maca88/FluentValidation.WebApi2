using System;
using System.Collections.Generic;
using System.Web.Http.Metadata;
using System.Web.Http.Validation;

namespace FluentValidation.WebApi2
{
    public interface IFluentModelValidator
    {
        IEnumerable<ModelValidationResult> Validate(ModelMetadata metadata, IValidationContext validationContext, object container);
    }
}
