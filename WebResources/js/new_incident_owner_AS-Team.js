//-when the Owner is a Team, the displayed team name in the Owner lookup field is shown in the correct language (Arabic/English) depending on the logged-in user’s language preference.-------------------------------------------
window.localizeOwnerLookupteam = function (executionContext) {
    var formContext = executionContext.getFormContext();
    var userLang = Xrm.Utility.getGlobalContext().userSettings.languageId;

    var ownerAttr = formContext.getAttribute("ownerid");
    if (!ownerAttr) return;

    var ownerVal = ownerAttr.getValue();
    if (!ownerVal || ownerVal.length === 0) return;

    var owner = ownerVal[0];

    // Only apply if the Owner is a Team
    if (owner.entityType === "team") {
        var teamId = owner.id.replace(/[{}]/g, "");

        // Fetch multilingual team names
        Xrm.WebApi.retrieveRecord("team", teamId, "?$select=new_name_en,new_name_ar").then(
            function (result) {
                var newName = owner.name; // Default name
                if (userLang === 1025 && result.new_name_ar) {
                    newName = result.new_name_ar; // Arabic name
                } else if (result.new_name_en) {
                    newName = result.new_name_en; // English name
                }

                // Update lookup ONLY if name changed
                if (owner.name !== newName) {
                    owner.name = newName;
                    ownerAttr.setValue([owner]);
                    ownerAttr.setSubmitMode("always"); // Ensure data is saved
                    console.log("✅ Owner name localized to: " + newName);
                }
            },
            function (error) {
                console.error("❌ Error fetching team multilingual name: " + error.message);
            }
        );
    }
};

window.ownerOnChange = function (executionContext) {
    // Delay to allow CRM to set lookup value
    setTimeout(function () {
        window.localizeOwnerLookup(executionContext);
    }, 500); // Increased to 500ms for reliability
};
