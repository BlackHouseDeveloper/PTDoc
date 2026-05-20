let mobileLayoutQuery = null;
let mobileLayoutReference = null;
let mobileLayoutChangeHandler = null;

function getMobileLayoutQuery() {
    mobileLayoutQuery ??= window.matchMedia("(max-width: 767px)");
    return mobileLayoutQuery;
}

export function isMobileLayout() {
    return getMobileLayoutQuery().matches;
}

export function initializeMobileLayout(reference) {
    disposeMobileLayout();

    mobileLayoutReference = reference;
    const query = getMobileLayoutQuery();
    mobileLayoutChangeHandler = (event) => {
        mobileLayoutReference
            ?.invokeMethodAsync("OnMobileLayoutChanged", event.matches)
            .catch(() => {});
    };

    if (query.addEventListener) {
        query.addEventListener("change", mobileLayoutChangeHandler);
    } else {
        query.addListener(mobileLayoutChangeHandler);
    }

    return query.matches;
}

export function disposeMobileLayout() {
    if (mobileLayoutQuery && mobileLayoutChangeHandler) {
        if (mobileLayoutQuery.removeEventListener) {
            mobileLayoutQuery.removeEventListener("change", mobileLayoutChangeHandler);
        } else {
            mobileLayoutQuery.removeListener(mobileLayoutChangeHandler);
        }
    }

    mobileLayoutReference = null;
    mobileLayoutChangeHandler = null;
}
