﻿using AutoMapper;
using AutoMapper.QueryableExtensions;
using Ezenity.Core.Entities.EmailTemplates;
using Ezenity.Core.Helpers.Exceptions;
using Ezenity.Core.Interfaces;
using Ezenity.Core.Services.Common;
using Ezenity.DTOs.Models;
using Ezenity.DTOs.Models.EmailTemplates;
using Ezenity.DTOs.Models.Pages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ezenity.Infrastructure.Services.EmailTemplates
{
    /// <summary>
    /// Provides functionality to manage Email Templates.
    /// </summary>
    public class EmailTemplateService : IEmailTemplateService
    {
        /// <summary>
        /// Provides data access to the application's data store.
        /// </summary>
        private readonly IDataContext _context;

        /// <summary>
        /// Provides object-object mapping functionality.
        /// </summary>
        private readonly IMapper _mapper;

        /// <summary>
        /// Provides access to application settings.
        /// </summary>
        private readonly IAppSettings _appSettings;

        /// <summary>
        /// Provides logging capabilities.
        /// </summary>
        private readonly ILogger<IEmailTemplateService> _logger;

        /// <summary>
        /// Provides token generation and validation services.
        /// </summary>
        private readonly ITokenHelper _tokenHelper;

        /// <summary>
        /// Provides user authentication services.
        /// </summary>
        private readonly IAuthService _authService;

        /// <summary>
        /// Initializes a new instance of the <see cref="EmailTemplateService"/> class.
        /// </summary>
        /// <param name="context">Provides data access to the application's data store.</param>
        /// <param name="mapper">Provides object-object mapping functionality.</param>
        /// <param name="appSettings">Provides access to application settings.</param>
        /// <param name="logger">Provides logging capabilities.</param>
        /// <param name="tokenHelper">Provides token generation and validation services.</param>
        /// <param name="authService">Provides user authentication services.</param>
        public EmailTemplateService(IDataContext context, IMapper mapper, IAppSettings appSettings, ILogger<IEmailTemplateService> logger, ITokenHelper tokenHelper, IAuthService authService)
        {
            _context = context ?? throw new ArgumentException(nameof(context));
            _mapper = mapper ?? throw new ArgumentException(nameof(mapper));
            _appSettings = appSettings ?? throw new ArgumentException(nameof(appSettings));
            _logger = logger ?? throw new ArgumentException(nameof(logger));
            _tokenHelper = tokenHelper ?? throw new ArgumentException(nameof(tokenHelper));
            _authService = authService ?? throw new ArgumentException(nameof(authService));
        }

        /// <summary>
        /// Fetches an email template by its ID.
        /// </summary>
        /// <param name="id">The ID of the email template to fetch.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains the EmailTemplateResponse.</returns>
        public async Task<EmailTemplateResponse> GetByIdAsync(int id)
        {
            //var emailTemplate = await GetEmailTemplate(id);
            var emailTemplate = await _context.EmailTemplates
                                    .Where(x => x.Id == id)
                                    .ProjectTo<EmailTemplateResponse>(_mapper.ConfigurationProvider)
                                    .SingleOrDefaultAsync();

            if (emailTemplate == null)
                throw new ResourceNotFoundException($"Email Template ID, {id}, was not found.");

            return _mapper.Map<EmailTemplateResponse>(emailTemplate);
        }

        public async Task<EmailTemplateNonDynamicResponse> GetNonDynamicByIdAsync(int id)
        {
            //var emailTemplate = await GetEmailTemplate(id);
            var emailTemplate = await _context.EmailTemplates
                                    .Where(x => x.Id == id)
                                    .ProjectTo<EmailTemplateNonDynamicResponse>(_mapper.ConfigurationProvider)
                                    .SingleOrDefaultAsync();

            if (emailTemplate == null)
                throw new ResourceNotFoundException($"Email Template ID, {id}, was not found.");

            return _mapper.Map<EmailTemplateNonDynamicResponse>(emailTemplate);
        }

        /// <summary>
        /// Creates a new email template.
        /// </summary>
        /// <param name="model">The details for the new email template.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains the EmailTemplateResponse for the newly created template.</returns>
        public async Task<EmailTemplateResponse> CreateAsync(CreateEmailTemplateRequest model)
        {
            // Validate
            if (await _context.EmailTemplates.AnyAsync(x => x.TemplateName == model.TemplateName))
                throw new ResourceAlreadyExistsException($"The Email Template name, '{model.TemplateName}', already exist. Please try a different Template Name.");

            // Map model to new email template object
            var emailTemplate = _mapper.Map<EmailTemplate>(model);

            emailTemplate.CreatedAt = DateTime.UtcNow;

            // save to database
            _context.EmailTemplates.Add(emailTemplate);
            await _context.SaveChangesAsync();

            return _mapper.Map<EmailTemplateResponse>(emailTemplate);
        }

        /// <summary>
        /// Deletes an email template by its ID.
        /// </summary>
        /// <param name="id">The ID of the email template to delete.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains the DeleteResponse.</returns>
        public async Task<DeleteResponse> DeleteAsync(int id)
        {
            var emailTemplate = await GetEmailTemplate(id);

            // TODO: Implement deleted data information
            /*deleteResponse.Message = "Email Template delet succesfully";
            deleteResponse.StatusCode = 200;
            deleteResponse.DeletedBy = account;
            deleteResponse.DeletedAt = DateTime.UtcNow;
            deleteResponse.ResourceId = DeleteEmailTemplateId.ToString();
            deleteResponse.IsSuccess = true;*/

            _context.EmailTemplates.Remove(emailTemplate);
            _context.SaveChanges();

            return _mapper.Map<DeleteResponse>(emailTemplate);
        }

        /// <summary>
        /// Fetches all email templates.
        /// </summary>
        /// <returns>A task that represents the asynchronous operation. The task result contains a list of EmailTemplateResponses.</returns>
        public async Task<IEnumerable<EmailTemplateResponse>> GetAllAsync()
        {
            var emailTemplate = await _context.EmailTemplates
                                    .ProjectTo<EmailTemplateResponse>(_mapper.ConfigurationProvider)
                                    .ToListAsync();

            return _mapper.Map<IList<EmailTemplateResponse>>(emailTemplate);
        }

        public async Task<PagedResult<EmailTemplateResponse>> GetAllAsync(string? name, string? searchQuery, int pageNumber, int pageSize)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Updates an existing email template.
        /// </summary>
        /// <param name="id">The ID of the email template to update.</param>
        /// <param name="model">The updated details for the email template.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains the updated EmailTemplateResponse.</returns>
        public async Task<EmailTemplateResponse> UpdateAsync(int id, UpdateEmailTemplateRequest model)
        {
            var emailTemplate = await GetEmailTemplate(id);

            // Validate
            if (emailTemplate.TemplateName != model.TemplateName && _context.EmailTemplates.Any(x => x.TemplateName == model.TemplateName))
                throw new ResourceAlreadyExistsException($"The Template name, '{model.TemplateName}', already exist, please try a different template name.");

            // update commin props
            _mapper.Map(model, emailTemplate);

            emailTemplate.UpdatedAt = DateTime.UtcNow;

            _context.EmailTemplates.Update(emailTemplate);
            await _context.SaveChangesAsync();

            return _mapper.Map<EmailTemplateResponse>(emailTemplate);
        }


        /// //////////////////
        /// Helper Methods ///
        /// //////////////////

        /// <summary>
        /// Helper method to fetch an email template by its ID.
        /// </summary>
        /// <param name="id">The ID of the email template to fetch.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains the EmailTemplate.</returns>
        private async Task<EmailTemplate> GetEmailTemplate(int id)
        {
            var emailTemplate = await _context.EmailTemplates.FindAsync(id);

            if (emailTemplate == null) throw new ResourceNotFoundException($"Email Template ID, {id}, not found");

            return emailTemplate;
        }
    }
}
