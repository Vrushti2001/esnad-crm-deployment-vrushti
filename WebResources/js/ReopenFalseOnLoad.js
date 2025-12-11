function isCurrentUserAllowedForReopen(primaryControl) {
    try {
        var formContext = primaryControl;
        var allowedTeamIds = [
            "953dd3b2-544b-f011-a3fe-d4de6fab9c57", // Dev D365Dev
            "2c80efda-7c4b-f011-a3ff-af212fee8ea9", // Dev Customer service team
            "0eb23b1a-a967-f011-a409-87895d8b1d04", // Prod-D365 team
            "16e9dd1a-b267-f011-a409-87895d8b1d04"  // Prod-Customer service team
        ];

        // ✅ 1. Get statecode of case (must be Resolved)
        var stateAttr = formContext.getAttribute("statecode");
        var stateValue = stateAttr ? stateAttr.getValue() : null;
        console.log("📌 Case statecode:", stateValue);

        // Show only if Resolved
        if (stateValue !== 1) {
            console.log("❌ Case is not resolved → Hide button");
            return false;
        }

        // ✅ 2. Get current logged-in user ID
        var userId = Xrm.Utility.getGlobalContext().userSettings.userId;
        userId = userId.replace(/[{}]/g, "").toLowerCase();
        console.log("👤 Logged-in User ID:", userId);

        // ✅ 3. Fetch user's team memberships via sync Web API
        var req = new XMLHttpRequest();
        var query = `/teammemberships?$select=teamid&$filter=systemuserid eq ${userId}`;
        var url = Xrm.Utility.getGlobalContext().getClientUrl() + "/api/data/v9.2" + query;

        req.open("GET", url, false); // synchronous
        req.setRequestHeader("OData-MaxVersion", "4.0");
        req.setRequestHeader("OData-Version", "4.0");
        req.setRequestHeader("Accept", "application/json");
        req.setRequestHeader("Content-Type", "application/json; charset=utf-8");
        req.send();

        if (req.status === 200) {
            var result = JSON.parse(req.responseText);
            var teamIds = result.value.map(t => t.teamid.toLowerCase());
            console.log("📦 Logged-in user's team IDs:", teamIds);

            var isAllowed = teamIds.some(id => allowedTeamIds.includes(id));
            console.log("✅ User in allowed team?", isAllowed);

            return isAllowed;
        } else {
            console.error("🚨 Error fetching team memberships:", req.statusText);
            return false;
        }
    } catch (e) {
        console.error("🔥 Error in isCurrentUserAllowedForReopen:", e);
        return false;
    }
}
