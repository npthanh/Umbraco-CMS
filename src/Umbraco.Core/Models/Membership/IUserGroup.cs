using System.Collections;
using Umbraco.Cms.Core.Models.Entities;

namespace Umbraco.Cms.Core.Models.Membership;

public interface IUserGroup : IEntity, IRememberBeingDirty
{
    string Alias { get; set; }

    int? StartContentId { get; set; }

    int? StartMediaId { get; set; }

    /// <summary>
    ///     The icon
    /// </summary>
    string? Icon { get; set; }

    /// <summary>
    ///     The name
    /// </summary>
    string? Name { get; set; }

    /// <summary>
    ///     If this property is true it will give the group access to all languages
    /// </summary>
    /// This is set to return true as default to avoid breaking changes
    public bool HasAccessToAllLanguages
    {
        get => true;
        set { /* This is NoOp to avoid breaking changes */ }
    }

    /// <summary>
    ///     The set of default permissions
    /// </summary>
    /// <remarks>
    ///     By default each permission is simply a single char but we've made this an enumerable{string} to support a more
    ///     flexible permissions structure in the future.
    /// </remarks>
    IEnumerable<string>? Permissions { get; set; } // todo v14 remove when old backoffice is removed, is superseded by ContextualPermissions

    IEnumerable<string> AllowedSections { get; } // todo v14 try to move this into ContextualPermissions

    void RemoveAllowedSection(string sectionAlias);

    void AddAllowedSection(string sectionAlias);

    void ClearAllowedSections();

    IEnumerable<int> AllowedLanguages => Enumerable.Empty<int>();

    void RemoveAllowedLanguage(int languageId)
    {
    }

    void AddAllowedLanguage(int languageId)
    {
    }

    void ClearAllowedLanguages()
    {
    }

    /// <summary>
    ///     Specifies the number of users assigned to this group
    /// </summary>
    int UserCount { get; }

    ISet<ContextualPermission> ContextualPermissions { get; set; } // todo v14 rename to permissions when others have been cleaned up
}
