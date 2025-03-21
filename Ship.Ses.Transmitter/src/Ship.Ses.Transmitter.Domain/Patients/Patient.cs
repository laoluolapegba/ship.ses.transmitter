using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Domain.Patients
{
    public class Patient : Entity
    {
        public Patient() { }
        public string PatientId { get; set; }
        public string Firstname { get; set; }
        public string Othername { get; set; }
        public string Surname { get; set; }
        public string Gender { get; set; }
        public string EmailAddress { get; set; }
        public string PhoneNumber { get; set; }
        public string Address { get; set; }
        public string City { get; set; }
        public string Country { get; set; }
        public string PostalCode { get; set; }
        public string PrimaryCareProviderId { get; set; }
        public DateOnly DateOfBirth { get; set; }
        public DateOnly DateOfRegistration { get; set; }
        public DateOnly DateOfLastVisit { get; set; }
        public DateOnly DateOfDeath { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string CreatedBy { get; set; }
        public string UpdatedBy { get; set; }
        public string SyncStatus { get; set; }
        public string SyncMessage { get; set; }
        public string SyncVersionId { get; set; }
    }
}
