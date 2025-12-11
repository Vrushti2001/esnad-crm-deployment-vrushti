
function setFormTypeField(executionContext) {
    var formContext = executionContext.getFormContext();

    console.log("🔹 setFormTypeField triggered");

    // Run only on Create
    var formType = formContext.ui.getFormType();
    console.log("Form Type:", formType);
    if (formType !== 1) {
        console.log("⏹ Not Create form — exiting script.");
        return;
    }

    // Get current form GUID
    var currentForm = formContext.ui.formSelector.getCurrentItem();
    if (!currentForm) {
        console.error("❌ Unable to get current form.");
        return;
    }

    var formId = currentForm.getId();
    console.log("Current Form ID:", formId);

    // Replace these with your actual Form GUIDs
    
    var FORM1_GUID = "ECF85945-9ABB-407D-9E93-6F67BC6303B3"; // Esnad
	var FORM2_GUID = "5fa4879e-bd07-4c8d-9ca5-699d03e0b1ef"; // Investor
    // Option set values (check in customization → options)
    var OPTION_FORM1 = 0; // Example: Form1 option value
    var OPTION_FORM2 = 1; // Example: Form2 option value
    var OPTION_DEFAULT = 0; // optional default

    var selectedValue = OPTION_DEFAULT;

    // Match by form GUID
    if (formId && formId.toUpperCase() === FORM1_GUID.toUpperCase()) {
        selectedValue = OPTION_FORM1;
        console.log("✅ Matched Form 1 → Setting new_formtype =", selectedValue);
    } 
    else if (formId && formId.toUpperCase() === FORM2_GUID.toUpperCase()) {
        selectedValue = OPTION_FORM2;
        console.log("✅ Matched Form 2 → Setting new_formtype =", selectedValue);
    } 
    else {
        console.log("⚠️ No form match → Using default value =", selectedValue);
    }

    // Set the OptionSet field safely
    var fieldAttr = formContext.getAttribute("new_formtype");
    if (fieldAttr) {
        try {
            fieldAttr.setValue(selectedValue);
            console.log("🎯 new_formtype field updated successfully.");
        } catch (e) {
            console.error("❌ Error setting new_formtype:", e);
        }
    } else {
        console.error("❌ Field 'new_formtype' not found on form.");
    }

   
    console.log("✅ setFormTypeField execution completed.");
}
