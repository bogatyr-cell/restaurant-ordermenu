let cart = [];
let allOrders = [];
let currentAdminFilter = 'Все';

function switchView(view) {
    if (view === 'menu') {
        document.getElementById('viewMenu').style.display = 'grid';
        document.getElementById('viewAdmin').style.display = 'none';
        document.getElementById('tabMenu').classList.add('active');
        document.getElementById('tabAdmin').classList.remove('active');
    } else {
        document.getElementById('viewMenu').style.display = 'none';
        document.getElementById('viewAdmin').style.display = 'block';
        document.getElementById('tabMenu').classList.remove('active');
        document.getElementById('tabAdmin').classList.add('active');
        loadAdminOrders();
    }
}

async function init() {
    try {
        const resCat = await fetch('/api/menu/categories');
        const cats = await resCat.json();
        const catBox = document.getElementById('catTabs');
        catBox.innerHTML = cats.map((c, i) => `
      <button class="cat-btn ${i === 0 ? 'active' : ''}" onclick="setCategory('${c}', this)">${c}</button>
    `).join('');
    } catch (e) {
        console.error('Ошибка загрузки категорий', e);
    }

    loadMenu('Все');
}

function setCategory(cat, btn) {
    document.querySelectorAll('.cat-btn').forEach(b => b.classList.remove('active'));
    btn.classList.add('active');
    loadMenu(cat);
}

async function loadMenu(cat = 'Все') {
    try {
        const url = cat === 'Все' ? '/api/menu' : `/api/menu?category=${encodeURIComponent(cat)}`;
        const res = await fetch(url);
        const items = await res.json();
        document.getElementById('menuList').innerHTML = items.map(i => `
      <div class="card">
        <div class="card-img">
          <img src="${i.icon}" alt="${i.title}">
        </div>
        <h4>${i.title}</h4>
        <p style="font-size:0.8rem; color:var(--muted); margin:6px 0 12px;">${i.description}</p>
        <div class="card-footer">
          <span style="font-weight:700;">${i.price} ₽</span>
          <button class="btn-add" onclick="addToCart(${i.id}, '${i.title}', ${i.price})">+ В заказ</button>
        </div>
      </div>
    `).join('');
    } catch (e) {
        document.getElementById('menuList').innerHTML = '<p style="color:red;">Ошибка соединения с сервером.</p>';
    }
}

function addToCart(id, title, price) {
    const item = cart.find(x => x.menuItemId === id);
    if (item) {
        item.quantity++;
    } else {
        cart.push({ menuItemId: id, title, price, quantity: 1 });
    }
    renderCart();
}

function changeQty(id, delta) {
    const item = cart.find(x => x.menuItemId === id);
    if (!item) return;
    item.quantity += delta;
    if (item.quantity <= 0) cart = cart.filter(x => x.menuItemId !== id);
    renderCart();
}

function renderCart() {
    const box = document.getElementById('cartItems');
    const sum = document.getElementById('cartSum');
    const btn = document.getElementById('btnSend');

    if (cart.length === 0) {
        box.innerHTML = '<p style="color:#888; font-size:0.9rem;">Корзина пуста</p>';
        sum.textContent = '0';
        btn.disabled = true;
        return;
    }

    box.innerHTML = cart.map(i => `
    <div class="cart-item">
      <span>${i.title} × ${i.quantity}</span>
      <div>
        <button class="btn-qty" onclick="changeQty(${i.menuItemId}, -1)">-</button>
        <button class="btn-qty" onclick="changeQty(${i.menuItemId}, 1)">+</button>
        <strong style="margin-left:6px;">${i.price * i.quantity} ₽</strong>
      </div>
    </div>
  `).join('');

    const total = cart.reduce((s, i) => s + (i.price * i.quantity), 0);
    sum.textContent = total;
    btn.disabled = false;
}

async function submitOrder() {
    const msgBox = document.getElementById('orderStatusMsg');
    if (msgBox) msgBox.textContent = '';

    const tblInput = document.getElementById('tblNum');
    const guestInput = document.getElementById('guestName');

    const rawVal = tblInput.value.trim();
    const rawTable = parseInt(rawVal, 10);

    if (!rawVal || isNaN(rawTable) || rawTable < 1 || rawTable > 50) {
        if (msgBox) {
            msgBox.style.color = '#dc2626';
            msgBox.textContent = 'Укажите номер стола от 1 до 50';
        }
        tblInput.focus();
        return;
    }

    const payload = {
        customerName: guestInput.value.trim() || 'Гость',
        tableNumber: rawTable,
        items: cart
    };

    try {
        const res = await fetch('/api/orders', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(payload)
        });

        if (res.ok) {
            const data = await res.json();
            cart = [];
            renderCart();

            tblInput.value = '';
            guestInput.value = '';

            if (msgBox) {
                msgBox.style.color = '#16a34a';
                msgBox.textContent = `Заказ #${data.orderId} отправлен (${data.waitStaff})`;
                setTimeout(() => {
                    msgBox.textContent = '';
                }, 5000);
            }
        } else {
            const err = await res.json();
            if (msgBox) {
                msgBox.style.color = '#dc2626';
                msgBox.textContent = err.error || 'Ошибка отправки';
            }
        }
    } catch (e) {
        if (msgBox) {
            msgBox.style.color = '#dc2626';
            msgBox.textContent = 'Сбой связи с сервером';
        }
    }
}

async function loadAdminOrders() {
    const tbody = document.getElementById('adminTableBody');
    tbody.innerHTML = '<tr><td colspan="9" style="text-align:center; color:#64748b; padding:15px;">Загрузка списка заказов...</td></tr>';

    try {
        const res = await fetch(`/api/admin/orders?t=${Date.now()}`);
        if (!res.ok) throw new Error(`Статус: ${res.status}`);

        allOrders = await res.json();
        calculateStats(allOrders);
        renderAdminTable();
    } catch (e) {
        console.error(e);
        tbody.innerHTML = '<tr><td colspan="9" style="color:#dc2626; text-align:center; padding:15px;">Ошибка при чтении данных из базы</td></tr>';
    }
}

function calculateStats(orders) {
    const total = orders.length;
    const cooking = orders.filter(o => o.status === 'Готовится').length;
    const revenue = orders
        .filter(o => o.status === 'Выдан')
        .reduce((sum, o) => sum + o.totalAmount, 0);

    const elTotal = document.getElementById('statTotalCount');
    const elCooking = document.getElementById('statCookingCount');
    const elRevenue = document.getElementById('statRevenue');

    if (elTotal) elTotal.textContent = total;
    if (elCooking) elCooking.textContent = cooking;
    if (elRevenue) elRevenue.textContent = `${revenue} ₽`;
}

function setAdminFilter(status, btn) {
    currentAdminFilter = status;
    document.querySelectorAll('.filter-chip').forEach(b => b.classList.remove('active'));
    btn.classList.add('active');
    renderAdminTable();
}

function renderAdminTable() {
    const tbody = document.getElementById('adminTableBody');

    const filtered = currentAdminFilter === 'Все'
        ? allOrders
        : allOrders.filter(o => o.status === currentAdminFilter);

    if (!filtered || filtered.length === 0) {
        tbody.innerHTML = '<tr><td colspan="9" style="text-align:center; color:#64748b; padding:15px;">Заказов в данной категории пока нет.</td></tr>';
        return;
    }

    tbody.innerHTML = filtered.map(o => {
        const isCooking = o.status === 'Готовится';
        const nextStatus = isCooking ? 'Выдан' : 'Готовится';
        const itemsList = Array.isArray(o.items) && o.items.length > 0
            ? o.items.map(i => `${i.title} (${i.quantity})`).join(', ')
            : '—';

        return `
        <tr>
          <td><strong>#${o.id}</strong></td>
          <td style="font-size:0.85rem; color:#64748b;">${o.createdAt}</td>
          <td>Стол ${o.tableNumber}</td>
          <td>${o.customerName}</td>
          <td style="color:#334155; font-weight:500;">${o.waitStaff}</td>
          <td style="font-size:0.9rem;">${itemsList}</td>
          <td><strong>${o.totalAmount} ₽</strong></td>
          <td>
            <span class="badge ${isCooking ? 'badge-cooking' : 'badge-ready'}">
              ${o.status}
            </span>
          </td>
          <td id="actions-${o.id}">
            <button class="btn-status" onclick="updateStatus(${o.id}, '${nextStatus}')">
              ${isCooking ? 'Выдать' : 'Вернуть'}
            </button>
            <button class="btn-del" onclick="deleteOrder(${o.id})" title="Удалить">
              ✕
            </button>
          </td>
        </tr>
      `;
    }).join('');
}

async function updateStatus(id, newStatus) {
    try {
        await fetch(`/api/admin/orders/${id}/status`, {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ status: newStatus })
        });
        loadAdminOrders();
    } catch (e) {
        console.error('Не удалось изменить статус', e);
    }
}

function deleteOrder(id, confirmed = false) {
    const actionCell = document.getElementById(`actions-${id}`);
    if (!actionCell) return;

    if (!confirmed) {
        actionCell.innerHTML = `
            <span style="font-size:0.75rem; color:#dc2626; font-weight:600; margin-right:4px;">Удалить?</span>
            <button class="btn-del" style="background:#dc2626; color:#fff; border-color:#dc2626;" onclick="deleteOrder(${id}, true)">Да</button>
            <button class="btn-status" onclick="loadAdminOrders()">Нет</button>
        `;
        return;
    }

    fetch(`/api/admin/orders/${id}`, { method: 'DELETE' })
        .then(res => {
            if (res.ok) {
                loadAdminOrders();
            }
        })
        .catch(e => console.error('Не удалось удалить заказ', e));
}

init();