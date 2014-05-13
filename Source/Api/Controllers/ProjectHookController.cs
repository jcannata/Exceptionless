﻿#region Copyright 2014 Exceptionless

// This program is free software: you can redistribute it and/or modify it 
// under the terms of the GNU Affero General Public License as published 
// by the Free Software Foundation, either version 3 of the License, or 
// (at your option) any later version.
// 
//     http://www.gnu.org/licenses/agpl-3.0.html

#endregion

using System;
using System.Net;
using System.Web.Http;
using Exceptionless.Api.Controllers;
using Exceptionless.Core.Authorization;
using Exceptionless.Core.Billing;
using Exceptionless.Core.Repositories;
using Exceptionless.Core.Web;
using Exceptionless.Models;
using Exceptionless.Models.Admin;
using Newtonsoft.Json.Linq;

namespace Exceptionless.App.Controllers.API {
    [RoutePrefix(API_PREFIX + "projecthook")]
    [Authorize(Roles = AuthorizationRoles.User)]
    public class ProjectHookController : RepositoryApiController<IProjectHookRepository, ProjectHook, ProjectHook, ProjectHook, ProjectHook> {
        private readonly IProjectRepository _projectRepository;
        private readonly BillingManager _billingManager;

        public ProjectHookController(IProjectHookRepository repository, IProjectRepository projectRepository, BillingManager billingManager) : base(repository) {
            _projectRepository = projectRepository;
            _billingManager = billingManager;
        }

        #region CRUD

        [HttpGet]
        [Route]
        public override IHttpActionResult GetById(string id) {
            return base.GetById(id);
        }

        [HttpPost]
        [Route]
        public override IHttpActionResult Post(ProjectHook value) {
            return base.Post(value);
        }

        [HttpPatch]
        [HttpPut]
        [Route]
        public override IHttpActionResult Patch(string id, Delta<ProjectHook> changes) {
            return base.Patch(id, changes);
        }

        [HttpDelete]
        [Route]
        public override IHttpActionResult Delete(string id) {
            return base.Delete(id);
        }

        #endregion

        [HttpGet]
        [Route("project/{projectId}")]
        public IHttpActionResult GetByProject(string projectId) {
            if (!IsInProject(projectId))
                return NotFound();

            return Ok(_repository.GetByProjectId(projectId));
        }

        /// <summary>
        /// This controller action is called by zapier to create a hook subscription.
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        [HttpPost]
        [Route("subscribe")]
        [OverrideAuthorization]
        [Authorize(Roles = AuthorizationRoles.UserOrClient)]
        public IHttpActionResult Subscribe(JObject data) {
            var targetUrl = data.GetValue("target_url").Value<string>();
            var eventType = data.GetValue("event").Value<string>();

            // TODO: Implement Subscribe.
            var project = Project;
            if (project != null) {
                _repository.Add(new ProjectHook {
                    EventTypes = new[] { eventType },
                    ProjectId = project.Id,
                    Url = targetUrl
                });
            }

            return StatusCode(HttpStatusCode.Created);
        }

        /// <summary>
        /// This controller action is called by zapier to remove a hook subscription.
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        [HttpPost]
        [AllowAnonymous]
        [Route("unsubscribe")]
        public IHttpActionResult Unsubscribe(JObject data) {
            var targetUrl = data.GetValue("target_url").Value<string>();

            // don't let this anon method delete non-zapier hooks
            if (!targetUrl.Contains("zapier"))
                return NotFound();

            // TODO: Validate that a user owns this webhook.
            _repository.RemoveByUrl(targetUrl);

            return Ok();
        }

        /// <summary>
        /// This controller action is called by zapier to test auth.
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [HttpPost]
        [Route("test")]
        [OverrideAuthorization]
        [Authorize(Roles = AuthorizationRoles.UserOrClient)]
        public IHttpActionResult Test() {
            return Ok(new[] {
                new { id = 1, Message = "Test message 1." },
                new { id = 2, Message = "Test message 2." }
            });
        }

        protected override ProjectHook GetModel(string id, bool useCache = true) {
            var model = base.GetModel(id);
            return model != null && IsInProject(model.ProjectId) ? model : null;
        }

        protected override PermissionResult CanAdd(ProjectHook value) {
            if (String.IsNullOrEmpty(value.ProjectId))
                return PermissionResult.DenyWithResult(BadRequest());

            Project project = _projectRepository.GetById(value.ProjectId, true);
            if (!IsInProject(project))
                return PermissionResult.DenyWithResult(BadRequest());

            if (!_billingManager.CanAddIntegration(project))
                return PermissionResult.DenyWithResult(PlanLimitReached("Please upgrade your plan to add integrations."));

            return base.CanAdd(value);
        }

        protected override PermissionResult CanUpdate(ProjectHook original, Delta<ProjectHook> changes) {
            if (!IsInProject(original.ProjectId))
                return PermissionResult.DenyWithResult(BadRequest());
            
            return base.CanUpdate(original, changes);
        }

        protected override PermissionResult CanDelete(ProjectHook value) {
            if (!IsInProject(value.ProjectId))
                return PermissionResult.DenyWithResult(BadRequest());

            return base.CanDelete(value);
        }

        private bool IsInProject(string projectId) {
            if (String.IsNullOrEmpty(projectId))
                return false;

            return IsInProject(_projectRepository.GetById(projectId, true));
        }

        private bool IsInProject(Project value) {
            if (value == null)
                return false;

            return IsInOrganization(value.OrganizationId);
        }
    }
}