using Ship.Ses.Transmitter.Domain.Customers.Exceptions;
using Ship.Ses.Transmitter.Domain.Orders;

namespace Ship.Ses.Transmitter.Domain.Patients
{
    public sealed record FullName
    {
        public string Value { get; init; }
        public FullName(string fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName) || fullName.Length < PatientConstants.Patient.FullNameMinLength || fullName.Length > PatientConstants.Patient.FullNameMaxLength)
            {
                throw new InvalidFullNameDomainException(fullName);
            }
            Value = fullName;
        }
        public static implicit operator string(FullName fullName) => fullName.Value;
        public static implicit operator FullName(string value) => new FullName(value);
    }
}
