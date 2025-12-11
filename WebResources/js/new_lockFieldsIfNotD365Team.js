function lockFieldsIfNotD365Team(executionContext) {
    console.log("⏳ Delaying execution by 1 second to allow resources to load...");
    setTimeout(function () {
        var formContext = executionContext.getFormContext();
        console.log("✅ Script started: Checking current user's team membership.");

        // ✅ Allowed Team GUIDs (Lowercase)
        var allowedTeamIds = [
            "953dd3b2-544b-f011-a3fe-d4de6fab9c57", // Dev D365Dev
            "2c80efda-7c4b-f011-a3ff-af212fee8ea9", // Dev Customer service team
            "230121da-a673-f011-a40d-c0b1f6211923",//Dev Service Agents Teams 
            "2ab2932b-9f73-f011-a40d-c0b1f6211923",//Dev Service Agents Department
            "0eb23b1a-a967-f011-a409-87895d8b1d04", // Prod-D365 team
            "16e9dd1a-b267-f011-a409-87895d8b1d04",  // Prod-Customer service team
            "9a685a34-a967-f011-a409-87895d8b1d04",  // Prod-Service Agents Teams
            "d5f16e18-b4bf-f011-a42c-f76dbbe58aa4" //KI: Relationship Manager Team dev
        ];
        console.log("Allowed Team IDs:", allowedTeamIds);

        // ✅ Get Current Logged-in User ID
        var userId = Xrm.Utility.getGlobalContext().userSettings.userId.replace("{", "").replace("}", "").toLowerCase();
        console.log("Current Logged-in User ID:", userId);

        // ✅ Fetch user's team memberships
        fetchUserTeams(userId).then(userTeams => {
            console.log("✅ Teams current user belongs to:", userTeams);

            // ✅ Check if user belongs to ANY allowed team
            var belongsToAllowedTeam = userTeams.some(teamId => allowedTeamIds.includes(teamId));

            if (belongsToAllowedTeam) {
                console.log("✅ Current user belongs to an allowed team → Fields remain editable.");
                setAllFieldsDisabled(formContext, false); // Editable
            } else {
                console.log("❌ Current user does NOT belong to any allowed team → Locking all fields.");
                setAllFieldsDisabled(formContext, true); // Disabled
            }

            // ✅ Find "Ticket Number" field and make it read-only (disabled)
            var ticketNumberControl = formContext.getControl("ticketnumber");
            if (ticketNumberControl) {
                ticketNumberControl.setDisabled(true); // Make it read-only
                console.log("✅ Ticket Number field is now read-only.");
            } else {
                console.warn("❌ Ticket Number field not found.");
            }
			var stateCodeControl = formContext.getControl("statecode");
			if (stateCodeControl) {
				stateCodeControl.setDisabled(true);
				console.log("✅ State Code field is now read-only.");
			} else {
				console.warn("❌ State Code field not found.");
			}

			var statusCodeControl = formContext.getControl("statuscode");
			if (statusCodeControl) {
				statusCodeControl.setDisabled(true);
				console.log("✅ Status Code field is now read-only.");
			} else {
				console.warn("❌ Status Code field not found.");
			}

        }).catch(error => {
            console.error("❌ Error fetching user teams:", error);
        });
    }, 1000); // ✅ 1-second delay
}

// ✅ Fetch Teams of a given user using Web API
function fetchUserTeams(userId) {
    return new Promise((resolve, reject) => {
        Xrm.WebApi.retrieveMultipleRecords("teammembership", `?$select=teamid&$filter=systemuserid eq ${userId}`).then(
            function success(result) {
                console.log("✅ API Response:", result.entities);
                var teamIds = result.entities.map(e => e.teamid.toLowerCase());
                resolve(teamIds);
            },
            function (error) {
                console.error("❌ Error in fetchUserTeams:", error.message);
                reject(error.message);
            }
        );
    });
}

// ✅ Lock or Unlock All Fields
function setAllFieldsDisabled(formContext, isDisabled) {
    console.log(`🔒 Setting all fields to ${isDisabled ? "DISABLED" : "ENABLED"} mode.`);
    formContext.ui.controls.forEach(function (control) {
        if (control && control.setDisabled) {
            control.setDisabled(isDisabled);
            console.log(`Field "${control.getName()}" is now ${isDisabled ? "locked" : "editable"}.`);
        }
    });
    console.log("✅ All fields updated successfully.");
}
