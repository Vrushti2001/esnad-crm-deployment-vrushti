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
// Localize Main Classification lookup based on user language
window.localizeMainClassificationLookup = function (executionContext) {
    var formContext = executionContext.getFormContext();
    var userLang = Xrm.Utility.getGlobalContext().userSettings.languageId;

    var classificationAttr = formContext.getAttribute("new_mainclassification");
    if (!classificationAttr) return;

    var classificationVal = classificationAttr.getValue();
    if (!classificationVal || classificationVal.length === 0) return;

    var classification = classificationVal[0];
    var recordId = classification.id.replace(/[{}]/g, "");

    // Fetch multilingual names from new_mainclassification entity
    Xrm.WebApi.retrieveRecord(
        "new_mainclassification",
        recordId,
        "?$select=new_name_en,new_name_ar"
    ).then(
        function (result) {
            var newName = classification.name; // default

            if (userLang === 1025 && result.new_name_ar) {
                newName = result.new_name_ar; // Arabic
            } else if (result.new_name_en) {
                newName = result.new_name_en; // English
            }

            // Update lookup ONLY if name changed
            if (classification.name !== newName) {
                classification.name = newName;
                classificationAttr.setValue([classification]);
                classificationAttr.setSubmitMode("always");

                console.log("✅ Main Classification localized to: " + newName);
            }
        },
        function (error) {
            console.error("❌ Error fetching Main Classification name: " + error.message);
        }
    );
};

// Localize Sub Classification Item lookup based on user language
window.localizeSubClassificationItemLookup = function (executionContext) {
    var formContext = executionContext.getFormContext();
    var userLang = Xrm.Utility.getGlobalContext().userSettings.languageId;

    var subClassAttr = formContext.getAttribute("new_subclassificationitem");
    if (!subClassAttr) return;

    var subClassVal = subClassAttr.getValue();
    if (!subClassVal || subClassVal.length === 0) return;

    var subClass = subClassVal[0];
    var recordId = subClass.id.replace(/[{}]/g, "");

    // Fetch multilingual names from new_subclassificationitem entity
    Xrm.WebApi.retrieveRecord(
        "new_subclassificationitem",
        recordId,
        "?$select=new_name_en,new_name_ar"
    ).then(
        function (result) {
            var newName = subClass.name; // default CRM name

            if (userLang === 1025 && result.new_name_ar) {
                newName = result.new_name_ar; // Arabic
            } else if (result.new_name_en) {
                newName = result.new_name_en; // English
            }

            // Update lookup ONLY if name changed
            if (subClass.name !== newName) {
                subClass.name = newName;
                subClassAttr.setValue([subClass]);
                subClassAttr.setSubmitMode("always");

                console.log("✅ Sub Classification Item localized to: " + newName);
            }
        },
        function (error) {
            console.error("❌ Error fetching Sub Classification Item name: " + error.message);
        }
    );
};
// Localize Ticket Type lookup based on user language
window.localizeTicketTypeLookup = function (executionContext) {
    var formContext = executionContext.getFormContext();
    var userLang = Xrm.Utility.getGlobalContext().userSettings.languageId;

    var ticketTypeAttr = formContext.getAttribute("new_tickettype");
    if (!ticketTypeAttr) return;

    var ticketTypeVal = ticketTypeAttr.getValue();
    if (!ticketTypeVal || ticketTypeVal.length === 0) return;

    var ticketType = ticketTypeVal[0];
    var recordId = ticketType.id.replace(/[{}]/g, "");

    // Fetch multilingual names from new_tickettype entity
    Xrm.WebApi.retrieveRecord(
        "new_tickettype",
        recordId,
        "?$select=new_name_en,new_name_ar"
    ).then(
        function (result) {
            var newName = ticketType.name; // default CRM name

            if (userLang === 1025 && result.new_name_ar) {
                newName = result.new_name_ar; // Arabic
            } else if (result.new_name_en) {
                newName = result.new_name_en; // English
            }

            // Update lookup ONLY if name changed
            if (ticketType.name !== newName) {
                ticketType.name = newName;
                ticketTypeAttr.setValue([ticketType]);
                ticketTypeAttr.setSubmitMode("always");

                console.log("✅ Ticket Type localized to: " + newName);
            }
        },
        function (error) {
            console.error("❌ Error fetching Ticket Type name: " + error.message);
        }
    );
};
// Localize Sub Classification 2 lookup based on user language
window.localizeSubClassification2Lookup = function (executionContext) {
    var formContext = executionContext.getFormContext();
    var userLang = Xrm.Utility.getGlobalContext().userSettings.languageId;

    var subClass2Attr = formContext.getAttribute("new_subclassification2");
    if (!subClass2Attr) return;

    var subClass2Val = subClass2Attr.getValue();
    if (!subClass2Val || subClass2Val.length === 0) return;

    var subClass2 = subClass2Val[0];
    var recordId = subClass2.id.replace(/[{}]/g, "");

    // Fetch multilingual names from new_subclassification2 entity
    Xrm.WebApi.retrieveRecord(
        "new_subclassification2",
        recordId,
        "?$select=new_name_en,new_name_ar"
    ).then(
        function (result) {
            var newName = subClass2.name; // default CRM name

            if (userLang === 1025 && result.new_name_ar) {
                newName = result.new_name_ar; // Arabic
            } else if (result.new_name_en) {
                newName = result.new_name_en; // English
            }

            // Update lookup ONLY if name changed
            if (subClass2.name !== newName) {
                subClass2.name = newName;
                subClass2Attr.setValue([subClass2]);
                subClass2Attr.setSubmitMode("always");

                console.log("✅ Sub Classification 2 localized to: " + newName);
            }
        },
        function (error) {
            console.error("❌ Error fetching Sub Classification 2 name: " + error.message);
        }
    );
};
// Localize Support Type lookup based on user language
window.localizeSupportTypeLookup = function (executionContext) {
    var formContext = executionContext.getFormContext();
    var userLang = Xrm.Utility.getGlobalContext().userSettings.languageId;

    var supportTypeAttr = formContext.getAttribute("new_supporttype");
    if (!supportTypeAttr) return;

    var supportTypeVal = supportTypeAttr.getValue();
    if (!supportTypeVal || supportTypeVal.length === 0) return;

    var supportType = supportTypeVal[0];
    var recordId = supportType.id.replace(/[{}]/g, "");

    // Fetch multilingual names from new_supporttype entity
    Xrm.WebApi.retrieveRecord(
        "new_supporttype",
        recordId,
        "?$select=new_name_en,new_name_ar"
    ).then(
        function (result) {
            var newName = supportType.name; // default CRM name

            if (userLang === 1025 && result.new_name_ar) {
                newName = result.new_name_ar; // Arabic
            } else if (result.new_name_en) {
                newName = result.new_name_en; // English
            }

            // Update lookup ONLY if name changed
            if (supportType.name !== newName) {
                supportType.name = newName;
                supportTypeAttr.setValue([supportType]);
                supportTypeAttr.setSubmitMode("always");

                console.log("✅ Support Type localized to: " + newName);
            }
        },
        function (error) {
            console.error("❌ Error fetching Support Type name: " + error.message);
        }
    );
};
