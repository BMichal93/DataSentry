namespace DataSentry.Core.Models;

/// <summary>
/// The kind of personal data a detector found. Ordered by the consequence of getting it wrong:
/// <see cref="SpecialCategory"/> drives the recommendation ahead of every other category.
/// </summary>
public enum PiiCategory
{
    /// <summary>Health, biometric, genetic, racial or ethnic origin, political opinion, religious belief, trade union membership, sex life or sexual orientation (GDPR Art. 9).</summary>
    SpecialCategory,

    /// <summary>IBAN, payment card number.</summary>
    Financial,

    /// <summary>PESEL, SSN, NINO, passport number.</summary>
    Identity,

    /// <summary>Name, email address, phone number, postal address.</summary>
    Contact,

    /// <summary>IP address.</summary>
    Network,

    /// <summary>A term-list hit that does not fit a stricter category.</summary>
    Keyword
}
