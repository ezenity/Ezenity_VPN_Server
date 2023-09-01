﻿using System;

namespace Ezenity_Backend.Models.Common.EmailTemplates
{
    public interface IEmailTemplateResponse
    {
        int Id { get; set; }
        string TemplateName { get; set; }
        string Subject { get; set; }
        string Content { get; set; }
        bool IsDefault { get; set; }
        bool IsDynamic { get; set; }
        DateTime? StartDate { get; set; }
        DateTime? EndDate { get; set; }
        bool IsActive { get; set; }
    }
}