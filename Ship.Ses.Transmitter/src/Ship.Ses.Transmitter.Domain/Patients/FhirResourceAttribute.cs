using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Domain.Patients
{
    //explicit resource naming for discovery
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class FhirResourceAttribute : Attribute
    {
        public string ResourceName { get; }
        public FhirResourceAttribute(string resourceName) => ResourceName = resourceName;
    }

}
