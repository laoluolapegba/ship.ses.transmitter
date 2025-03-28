﻿namespace Ship.Ses.Transmitter.Domain.Patients
{
    public class PatientConstants
    {
        public class Patient
        {
            public const int PostalCodeMaxLength = 6;
            public const int StreetMaxLength = 255;
            public const int HouseNumberMaxLength = 15;
            public const int FlatNumberMaxLength = 15;
            public const int CountryMaxLength = 100;
            public const int MinAllowedAge = 18;
            public const int FullNameMinLength = 3;
            public const int FullNameMaxLength = 150;
            public const int EmailMaxLength = 400;
        }
    }
}
