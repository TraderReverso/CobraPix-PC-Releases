using System;

namespace CobraPix_PC.Services.Exceptions
{
    /// <summary>
    /// Exceção lançada quando uma regra de negócio ou validação de domínio do CobraPix for violada.
    /// </summary>
    public class BusinessRuleException : Exception
    {
        public BusinessRuleException(string message) : base(message)
        {
        }

        public BusinessRuleException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
