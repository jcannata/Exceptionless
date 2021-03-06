﻿using System;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Exceptionless.Api.Extensions;
using Exceptionless.Core.Extensions;
using Exceptionless.Core.Repositories;
using Exceptionless.Models;

namespace Exceptionless.Api.Security {
    public class AuthMessageHandler : DelegatingHandler {
        public const string BearerScheme = "bearer";
        public const string BasicScheme = "basic";
        public const string TokenScheme = "token";

        private readonly TokenManager _tokenManager;
        private readonly IUserRepository _userRepository;
        private readonly SecurityEncoder _encoder;

        public AuthMessageHandler(TokenManager tokenManager, IUserRepository userRepository, SecurityEncoder encoder) {
            _tokenManager = tokenManager;
            _userRepository = userRepository;
            _encoder = encoder;
        }

        protected virtual Task<HttpResponseMessage> BaseSendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            return base.SendAsync(request, cancellationToken);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            var authHeader = request.Headers.Authorization;
            string scheme = authHeader != null ? authHeader.Scheme.ToLower() : null;
            string token = null;
            if (authHeader != null && (scheme == BearerScheme || scheme == TokenScheme))
                token = authHeader.Parameter;
            else if (authHeader != null && scheme == BasicScheme) {
                var authInfo = request.GetBasicAuth();
                if (authInfo != null) {
                    if (authInfo.Username.ToLower() == "client")
                        token = authInfo.Password;
                    else if (authInfo.Password.ToLower() == "x-oauth-basic" || String.IsNullOrEmpty(authInfo.Password))
                        token = authInfo.Username;
                    else {
                        User user;
                        try {
                            user = _userRepository.GetByEmailAddress(authInfo.Username);
                        } catch (Exception) {
                            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
                        }

                        if (user == null || !user.IsActive)
                            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));

                        if (String.IsNullOrEmpty(user.Salt))
                            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));

                        string encodedPassword = _encoder.GetSaltedHash(authInfo.Password, user.Salt);
                        if (!String.Equals(encodedPassword, user.Password))
                            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));

                        request.GetRequestContext().Principal = new ClaimsPrincipal(user.ToIdentity());

                        return BaseSendAsync(request, cancellationToken);
                    }
                }
            } else {
                string queryToken = request.GetQueryString("access_token");
                if (!String.IsNullOrEmpty(queryToken))
                    token = queryToken;
            }
            
            if (String.IsNullOrEmpty(token))
                return BaseSendAsync(request, cancellationToken);

            //try {
            IPrincipal principal = _tokenManager.Validate(token);
            if (principal != null)
                request.GetRequestContext().Principal = principal;
            
            //} catch (SecurityTokenExpiredException e) {
            //    _logger.ErrorFormat("Security token expired: {0}", e);

            //    var response = new HttpResponseMessage((HttpStatusCode)440) {
            //        Content = new StringContent("Security token expired exception")
            //    };

            //    var tsc = new TaskCompletionSource<HttpResponseMessage>();
            //    tsc.SetResult(response);
            //    return tsc.Task;
            //} catch (SecurityTokenSignatureKeyNotFoundException e) {
            //    _logger.ErrorFormat("Error during JWT validation: {0}", e);

            //    var response = new HttpResponseMessage(HttpStatusCode.Unauthorized) {
            //        Content = new StringContent("Untrusted signing cert")
            //    };

            //    var tsc = new TaskCompletionSource<HttpResponseMessage>();
            //    tsc.SetResult(response);
            //    return tsc.Task;
            //} catch (SecurityTokenValidationException e) {
            //    _logger.ErrorFormat("Error during JWT validation: {0}", e);
            //    throw;
            //} catch (Exception e) {
            //    _logger.ErrorFormat("Error during JWT validation: {0}", e);
            //    throw;
            //}

            return BaseSendAsync(request, cancellationToken);
        }
    }
}