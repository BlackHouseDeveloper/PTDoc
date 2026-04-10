window.ptdocAuth = {
  submitLogin: (username, pin, returnUrl) => {
    const form = document.createElement("form");
    form.method = "post";
    form.action = "/auth/login";

    const fields = {
      username: username || "",
      pin: pin || "",
      returnUrl: returnUrl || "/"
    };

    Object.entries(fields).forEach(([name, value]) => {
      const input = document.createElement("input");
      input.type = "hidden";
      input.name = name;
      input.value = value;
      form.appendChild(input);
    });

    document.body.appendChild(form);
    form.submit();
  },

  resetLoginFields: () => {
    const syncBlankValue = (input) => {
      if (!input) {
        return;
      }

      input.value = "";
      input.dispatchEvent(new Event("input", { bubbles: true }));
      input.dispatchEvent(new Event("change", { bubbles: true }));
    };

    const usernameInput = document.getElementById("username");
    const pinInput = document.getElementById("pin");

    syncBlankValue(usernameInput);
    syncBlankValue(pinInput);

    if (usernameInput) {
      usernameInput.focus();
    }
  }
};
