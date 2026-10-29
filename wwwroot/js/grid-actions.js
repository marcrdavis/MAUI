/*
 * Author: Marc R. Davis (marcdavis@comcast.net)
 * Project: MAUI - MOVEit Automation UI
 * This JavaScript is part of the MAUI application.
 */

(() => {
    // Creates the shared toolbar/action-menu coordinator for one MAUI grid.
    function createManager(options) {
        const {
            grid,
            savedViewManager,
            savedViewApi,
            exporter,
            printer,
            reportApi,
            getSelectedIds,
            showFallbackToast
        } = options || {};

        if (!grid) throw new Error('MauiGridActions requires a grid element.');

        // Closes one toolbar dropdown and synchronizes its accessibility state.
        function closeMenu(menu) {
            if (!menu) return;
            menu.classList.remove('open');
            menu.querySelector('[data-menu-button]')?.setAttribute('aria-expanded', 'false');
        }

        // Closes every open toolbar dropdown in this grid except an optional menu being toggled.
        function closeMenusExcept(exceptMenu) {
            grid.querySelectorAll('[data-menu].open').forEach(menu => {
                if (menu !== exceptMenu) closeMenu(menu);
            });
        }

        // Opens or closes one toolbar dropdown after closing sibling menus.
        function toggleMenu(menu) {
            if (!menu || menu.querySelector('[data-menu-button]')?.disabled) return;
            closeMenusExcept(menu);
            window.MauiObjectAccess?.reset(menu);
            const isOpen = menu.classList.toggle('open');
            if (isOpen) void window.MauiObjectAccess?.bulk(grid,menu,typeof getSelectedIds === 'function' ? getSelectedIds() : []);
            menu.querySelector('[data-menu-button]')?.setAttribute('aria-expanded', isOpen ? 'true' : 'false');
        }

        // Dispatches the established cancelable grid-action event for page-specific bulk/add actions.
        function dispatchGridAction(button) {
            if (!button || button.disabled) return;
            const action = String(button.dataset.gridAction || '').trim();
            const actionEvent = new CustomEvent('maui:grid-action', {
                bubbles: true,
                cancelable: true,
                detail: {
                    action,
                    gridId: String(grid.dataset.grid || ''),
                    selectedIds: typeof getSelectedIds === 'function' ? getSelectedIds() : []
                }
            });

            const handledByPage = !grid.dispatchEvent(actionEvent);
            if (!handledByPage && typeof showFallbackToast === 'function') showFallbackToast(action);
            closeMenu(button?.closest?.('[data-menu]'));
        }

        // Runs a Saved View command and closes the originating toolbar menu.
        function runSavedViewCommand(button, mode) {
            if (mode === 'delete') savedViewManager?.openDelete?.(savedViewApi);
            else savedViewManager?.openSave?.(savedViewApi, mode);
            closeMenu(button?.closest?.('[data-menu]'));
        }

        // Exports the current grid view and closes the originating toolbar menu.
        function exportCsv(button) {
            exporter?.exportCurrentViewToCsv?.();
            closeMenu(button?.closest?.('[data-menu]'));
        }

        function printView(button) {
            if (!printer?.printCurrentView) {
                window.mauiShowToast?.('Print View is unavailable on this page.', 'error');
            } else {
                printer.printCurrentView();
            }
            closeMenu(button?.closest?.('[data-menu]'));
        }

        function createReport(button) {
            const editContext = reportApi?.getEditContext?.();
            if (editContext) {
                if (!window.MauiOutboundReports?.updateView) window.mauiShowToast?.('Report update is unavailable on this page.', 'error');
                else void window.MauiOutboundReports.updateView(reportApi, editContext);
            } else if (!window.MauiOutboundReports?.openCreate || !reportApi) {
                window.mauiShowToast?.('Report creation is unavailable on this page.', 'error');
            } else {
                window.MauiOutboundReports.openCreate(reportApi);
            }
            closeMenu(button?.closest?.('[data-menu]'));
        }

        // Wires shared toolbar menus and actions while leaving feature-specific row menus to page scripts.
        function wire() {
            grid.querySelectorAll('[data-menu-button]').forEach(button => {
                button.addEventListener('click', event => {
                    event.stopPropagation();
                    toggleMenu(button.closest('[data-menu]'));
                });
            });

            grid.querySelectorAll('[data-grid-action]').forEach(button => {
                button.addEventListener('click', () => dispatchGridAction(button));
            });

            grid.querySelector('[data-grid-create-view]')?.addEventListener('click', event => {
                runSavedViewCommand(event.currentTarget, 'create');
            });
            grid.querySelector('[data-grid-update-view]')?.addEventListener('click', event => {
                runSavedViewCommand(event.currentTarget, 'update');
            });
            grid.querySelector('[data-grid-delete-view]')?.addEventListener('click', event => {
                runSavedViewCommand(event.currentTarget, 'delete');
            });
            grid.querySelector('[data-grid-export-csv]')?.addEventListener('click', event => {
                exportCsv(event.currentTarget);
            });
            grid.querySelector('[data-grid-print-view]')?.addEventListener('click', event => {
                printView(event.currentTarget);
            });
            grid.querySelector('[data-grid-create-report]')?.addEventListener('click', event => {
                createReport(event.currentTarget);
            });

            document.addEventListener('click', event => {
                if (event.target.closest('[data-menu]')) return;
                closeMenusExcept(null);
            });
        }

        return {
            wire,
            closeMenu,
            toggleMenu
        };
    }

    window.MauiGridActions = { createManager };
})();
