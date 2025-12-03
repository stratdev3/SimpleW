using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;


namespace SimpleW {

    /// <summary>
    /// SslContext
    /// </summary>
    public sealed class SslContext {

        /// <summary>
        /// Supported Protocols
        /// </summary>
        public SslProtocols Protocols { get; }

        /// <summary>
        /// Certificate
        /// </summary>
        public X509Certificate2 Certificate { get; }

        /// <summary>
        /// Is Client Certificate Required
        /// </summary>
        public bool ClientCertificateRequired { get; }

        /// <summary>
        /// Is Checking for Certificate Revocation
        /// </summary>
        public bool CheckCertificateRevocation { get; }

        public RemoteCertificateValidationCallback? ClientCertificateValidation { get; }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="protocols"></param>
        /// <param name="certificate"></param>
        /// <param name="clientCertificateRequired"></param>
        /// <param name="checkCertificateRevocation"></param>
        /// <param name="clientCertificateValidation"></param>
        /// <exception cref="ArgumentNullException"></exception>
        public SslContext(
            SslProtocols protocols,
            X509Certificate2 certificate,
            bool clientCertificateRequired = false,
            bool checkCertificateRevocation = false,
            RemoteCertificateValidationCallback? clientCertificateValidation = null
        ) {
            Protocols = protocols;
            Certificate = certificate ?? throw new ArgumentNullException(nameof(certificate));
            ClientCertificateRequired = clientCertificateRequired;
            CheckCertificateRevocation = checkCertificateRevocation;
            ClientCertificateValidation = clientCertificateValidation;
        }

    }

}
