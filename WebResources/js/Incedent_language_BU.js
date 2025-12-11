
window.localizeOwnerLookupBusinessUnit = function (executionContext) {
    var formContext = executionContext.getFormContext();
    var userLang = Xrm.Utility.getGlobalContext().userSettings.languageId;

    var ownerAttr = formContext.getAttribute("new_businessunitid");
    if (!ownerAttr) return;

    var ownerVal = ownerAttr.getValue();
    if (!ownerVal || ownerVal.length === 0) return;

    var owner = ownerVal[0];

    // Only apply if the Owner is a Business Unit (not Team)
    if (owner.entityType === "businessunit") {
        var businessUnitId = owner.id.replace(/[{}]/g, "");

        // Fetch multilingual Business Unit names
        Xrm.WebApi.retrieveRecord("businessunit", businessUnitId, "?$select=new_name_en,new_name_ar").then(
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
                    console.log("✅ Owner name localized to Business Unit: " + newName);
                }
            },
            function (error) {
                console.error("❌ Error fetching Business Unit multilingual name: " + error.message);
            }
        );
    }
};

window.ownerOnChange = function (executionContext) {
    // Delay to allow CRM to set lookup value
    setTimeout(function () {
        window.localizeOwnerLookupBusinessUnit(executionContext);
    }, 500); // Increased to 500ms for reliability
};
