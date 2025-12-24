function validateIntegerField(executionContext) {
    var formContext = executionContext.getFormContext();
    var fieldName = "new_crnumber"; // replace with your field logical name
    var fieldValue = formContext.getAttribute(fieldName).getValue();

    console.log("validateIntegerField called for field:", fieldName);
    console.log("Current field value:", fieldValue);

    if (fieldValue !== null && fieldValue !== undefined && fieldValue !== "") {
        // Ensure it's treated as string for validation
        var stringValue = String(fieldValue).trim();

        // Regex: exactly 10 digits (0–9)
        var regex = /^\d{10}$/;

        if (!regex.test(stringValue)) {
            console.log("❌ Invalid value. Must be exactly 10 digits.");

            // Show notification
            formContext.getControl(fieldName).setNotification("CR Number must be exactly 10 digits.");

            // Prevent save if OnSave event
            if (executionContext.getEventArgs) {
                var eventArgs = executionContext.getEventArgs();
                if (eventArgs && eventArgs.preventDefault) {
                    eventArgs.preventDefault(); // blocks save
                    console.log("Save prevented due to invalid CR Number.");
                }
            }
            return false;
        } else {
            console.log("✅ Valid CR Number:", stringValue);
            // Clear notification if valid
            formContext.getControl(fieldName).clearNotification();
        }
    } else {
        console.log("Field is empty or null.");
        formContext.getControl(fieldName).clearNotification();
    }
}


function preventSaveIfCRDuplicate(executionContext) {
    var formContext = executionContext.getFormContext();
    var eventArgs = executionContext.getEventArgs();

    var crAttr = formContext.getAttribute("new_crnumber");
    if (!crAttr) return;

    var crNumber = crAttr.getValue();
    if (!crNumber) return;

    var recordId = formContext.data.entity.getId();
    if (recordId) {
        recordId = recordId.replace("{", "").replace("}", "");
    }

    // Clear previous notification
    formContext.ui.clearFormNotification("CR_DUPLICATE");

    var query =
        "?$select=accountid" +
        "&$filter=new_crnumber eq '" + crNumber + "'" +
        (recordId ? " and accountid ne " + recordId : "");

    // ❗ STOP SAVE until validation completes
    eventArgs.preventDefault();

    Xrm.WebApi.retrieveMultipleRecords("account", query).then(
        function success(result) {
            if (result.entities.length > 0) {
                // Duplicate found → BLOCK SAVE
                formContext.ui.setFormNotification(
                    "CR Number already exists. You cannot save this record.",
                    "ERROR",
                    "CR_DUPLICATE"
                );
            } else {
                // No duplicate → allow save
                formContext.ui.clearFormNotification("CR_DUPLICATE");
                formContext.data.save();
            }
        },
        function error(err) {
            console.error("CR duplicate check failed:", err.message);
        }
    );
}
