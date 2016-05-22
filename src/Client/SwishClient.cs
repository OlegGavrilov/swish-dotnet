﻿using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Client
{
    /// <summary>
    /// Swish client
    /// </summary>
    public class SwishClient
    {
        private readonly HttpClient _client;
        private const string PaymentPath = "swish-cpcapi/api/v1/paymentrequests/";
        private const string RefundPath = "swish-cpcapi/api/v1/refunds";

        /// <summary>
        /// Default constructor
        /// </summary>
        /// <param name="configuration">The client configuration</param>
        /// <param name="cert">The client certificate</param>
        /// <param name="caCert">Optional CA root certificate used to verify server certificate, if not provided, no server certificate validation will be done</param>
        public SwishClient(IConfiguration configuration, X509Certificate2 cert, X509Certificate2 caCert = null)
        {
            // Only TLS 1.1 works
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls11;

            var handler = new WebRequestHandler();
            handler.ClientCertificates.Add(cert);

            if (caCert != null)
            {
                SetupServerCertificateValidation(handler, caCert);
            }

            _client = new HttpClient(handler) {BaseAddress = configuration.BaseUri()};
        }

        /// <summary>
        /// Initializes the swish client to use production configuration
        /// </summary>
        /// <param name="cert">The client certificate</param>
        /// <param name="caCert">Optional CA root certificate used to verify server certificate, if not provided, no server certificate validation will be done</param>
        public SwishClient(X509Certificate2 cert, X509Certificate2 caCert = null)
            :this(new ProductionConfig(), cert, caCert)
        { }

        public SwishClient(HttpClient httpClient)
        {
            _client = httpClient;
        }

        public async Task<ECommercePaymentResponse> MakeECommercePaymentAsync(ECommercePaymentModel payment)
        {
            var response = await Post(payment, PaymentPath);
            var responseContent = await response.Content.ReadAsStringAsync();
            if (response.StatusCode == (HttpStatusCode)422)
            {
                throw new SwishException(responseContent);
            }

            response.EnsureSuccessStatusCode();

            return ExtractSwishResponse(response) as ECommercePaymentResponse;
        }



        public async Task<MCommercePaymentResponse> MakeMCommercePaymentAsync(MCommercePaymentModel payment)
        {
            var response = await Post(payment, PaymentPath);
            var responseContent = await response.Content.ReadAsStringAsync();
            if (response.StatusCode == (HttpStatusCode)422)
            {
                throw new SwishException(responseContent);
            }

            response.EnsureSuccessStatusCode();

            return ExtractMCommerceResponse(response);
        }

        public async Task<PaymentStatusModel> GetPaymentStatus(string id)
        {
            var uri = $"{PaymentPath}/{id}";
            var response = await Get(uri);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<PaymentStatusModel>(responseContent);

        }

        public async Task<PaymentResponse> MakeRefundAsync(RefundModel refund)
        {
            var response = await Post(refund, RefundPath);
            var responseContent = await response.Content.ReadAsStringAsync();
            if (response.StatusCode == (HttpStatusCode) 422)
            {
                throw new SwishException(responseContent);
            }

            response.EnsureSuccessStatusCode();

            return ExtractSwishResponse(response);
        }

        public async Task<RefundStatusModel> GetRefundStatus(string id)
        {
            var uri = $"{RefundPath}/{id}";
            var response = await Get(uri);

            var responseContent = await response.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<RefundStatusModel>(responseContent);
        }

        private PaymentResponse ExtractSwishResponse(HttpResponseMessage responseMessage)
        {
            var location = responseMessage.Headers.GetValues("Location").FirstOrDefault();
            var swishResponse = new ECommercePaymentResponse();
            if (location != null)
            {
                var id = location.Split('/').LastOrDefault();
                swishResponse.Location = location;
                swishResponse.Id = id;
            }

            return swishResponse;
        }

        private MCommercePaymentResponse ExtractMCommerceResponse(HttpResponseMessage responseMessage)
        {
            var token = responseMessage.Headers.GetValues("PaymentRequestToken").FirstOrDefault();
            var paymentResponse = ExtractSwishResponse(responseMessage);

            return new MCommercePaymentResponse()
            {
                Id = paymentResponse.Id,
                Location = paymentResponse.Location,
                Token = token
            };
        }

        private Task<HttpResponseMessage> Post<T>(T model, string path)
        {
            var json = JsonConvert.SerializeObject(model, new JsonSerializerSettings()
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver()
            });

            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = _client.PostAsync(path, content);

            return response;
        }

        private Task<HttpResponseMessage> Get(string path)
        {
            var response = _client.GetAsync(path);
            return response;
        }

        private static void SetupServerCertificateValidation(WebRequestHandler handler, X509Certificate2 caCert)
        {
            handler.ServerCertificateValidationCallback =
                (sender, certificate, chain, errors) =>
                {
                    var x509ChainElement = chain.ChainElements.OfType<X509ChainElement>().LastOrDefault();
                    if (x509ChainElement == null) return false;
                    var c = x509ChainElement.Certificate;
                    return c.Equals(caCert);
                };
        }


    }

    public class MCommercePaymentResponse : PaymentResponse
    {
        public string Token { get; set; }
    }

    public class ECommercePaymentResponse : PaymentResponse
    {
    }

    public class PaymentResponse
    {
        public string Id { get; set; }

        public string Location { get; set; }
    }
}
