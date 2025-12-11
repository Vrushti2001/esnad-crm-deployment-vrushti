function hidePriorityOptions(executionContext) {

    var formContext = executionContext.getFormContext();
console.log(formContext);
    var control = formContext.getControl("prioritycode"); // your optionset schema name
console.log("control");
console.log(control);
    if (!control) return;

    // list of option values to hide on this form
    var valuesToHide = [3, 4]; // example values

    valuesToHide.forEach(function (value) {
        control.removeOption(value);
    });
}
