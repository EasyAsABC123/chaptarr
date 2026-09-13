const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { test } = require('node:test');
const babel = require('@babel/core');
const React = require('react');

function load(relativePath, createAjaxRequest) {
  const filename = path.resolve(__dirname, '../src', relativePath);
  const { code } = babel.transformSync(fs.readFileSync(filename, 'utf8'), {
    filename,
    configFile: false,
    babelrc: false,
    presets: ['@babel/preset-env', '@babel/preset-react']
  });
  const module = { exports: {} };
  const requireMock = (name) => {
    if (name === 'react' || name === 'prop-types') return require(name);
    if (name === 'react-redux') return { connect: () => (component) => component };
    if (name === 'Utilities/createAjaxRequest') return createAjaxRequest;
    if (name === 'Utilities/String/translate') return (key) => key;
    if (name === 'Utilities/Object/getErrorMessage') return (_error, fallback) => fallback;
    if (name === 'Store/Actions/baseActions') return { updateItem: () => {} };
    return (props) => React.createElement('span', props);
  };
  vm.runInThisContext(`(function(require, module, exports) {${code}\n})`, { filename })(requireMock, module, module.exports);
  return module.exports.default;
}

function instance(Component, props) {
  const component = new Component(props);
  component.setState = (patch) => Object.assign(component.state, patch);
  return component;
}

function deferred() {
  let resolve, reject;
  const request = new Promise((yes, no) => { resolve = yes; reject = no; });
  request.done = (fn) => { request.then(fn, () => {}); return request; };
  request.always = (fn) => { request.then(fn, fn); return request; };
  return { request, resolve, reject, abortRequest() {} };
}

const series = { localSeriesId: '30', title: 'A Series', mediaType: 'ebook', links: [{ bookId: 11, position: '2' }, { bookId: 12, position: '10' }, { bookId: 13, position: '1' }] };
const bookRows = [
  { id: 12, title: 'Ten', mediaType: 'ebook', ebookMonitored: false },
  { id: 11, title: 'Two', mediaType: 'ebook', ebookMonitored: false },
  { id: 13, title: 'Audio copy', mediaType: 'audiobook', audiobookMonitored: false },
  { id: 99, title: 'Unrelated', mediaType: 'ebook', ebookMonitored: false }
];

function setupMonitor({ empty = false, ignoreWrite = false, failRead = false } = {}) {
  const calls = [];
  const rows = bookRows.map((book) => ({ ...book }));
  const updates = [];
  const ajax = (options) => {
    calls.push(options);
    const result = deferred();
    if (options.method === 'PUT') {
      const body = JSON.parse(options.data);
      if (!ignoreWrite) rows.filter((book) => body.bookIds.includes(book.id)).forEach((book) => { book.ebookMonitored = body.monitored; });
      result.resolve(undefined); // A successful HTTP response alone is not proof.
    } else if (failRead) result.reject({ status: 503 });
    else result.resolve(options.url.startsWith('/series/') ? { ...series, links: empty ? [] : series.links } : rows.map((book) => ({ ...book })));
    return result;
  };
  const Component = load('Series/Details/SeriesDetailsConnector.js', ajax);
  const component = instance(Component, { localSeriesId: '30', updateItem: (item) => updates.push(item) });
  return { component, calls, updates };
}

test('book series links use exact membership and format, retaining position', async () => {
  const result = deferred();
  let query;
  const Component = load('Book/Details/BookSeriesLinks.js', (options) => { query = options; return result; });
  const component = instance(Component, { bookId: 11, authorId: 2, mediaType: 'ebook', seriesTitle: 'A Series #2' });
  component.componentDidMount();
  result.resolve([series, { ...series, localSeriesId: '40', mediaType: 'audiobook' }, { ...series, localSeriesId: '50', links: [{ bookId: 99 }] }]);
  await result.request;
  assert.deepEqual(query.data, { authorId: 2, mediaType: 'ebook' });
  const rendered = component.render();
  assert.equal(rendered.length, 1);
  assert.equal(rendered[0].props.children[1].props.localSeriesId, '30');
  assert.equal(rendered[0].props.children[1].props.seriesName, 'A Series #2');
});

test('unresolved series keeps the label and standalone books show no series', () => {
  const Component = load('Book/Details/BookSeriesLinks.js', () => deferred());
  assert.equal(instance(Component, { seriesTitle: 'Unresolved #1' }).render(), 'Unresolved #1');
  assert.equal(instance(Component, {}).render(), null);
});

test('late responses from a previous book cannot replace the current series', async () => {
  const requests = [deferred(), deferred()];
  let next = 0;
  const Component = load('Book/Details/BookSeriesLinks.js', () => requests[next++]);
  const component = instance(Component, { bookId: 11, authorId: 2, mediaType: 'ebook' });
  component.componentDidMount();
  const previous = component.props;
  component.props = { ...previous, bookId: 99 };
  component.componentDidUpdate(previous);
  requests[1].resolve([]);
  await requests[1].request;
  requests[0].resolve([series]);
  await requests[0].request;
  assert.deepEqual(component.state.series, []);
});

test('direct series loading filters membership and format and sorts positions numerically', async () => {
  const { component, calls } = setupMonitor();
  await component.onRefreshPress();
  assert.equal(calls[0].url, '/series/30');
  assert.deepEqual(component.state.books.map((book) => book.id), [11, 12]);
});

test('an empty series never requests the entire book library', async () => {
  const { component, calls } = setupMonitor({ empty: true });
  await component.onRefreshPress();
  assert.equal(calls.length, 1);
  assert.deepEqual(component.state.books, []);
});

test('bulk monitoring writes only eligible books and verifies the saved state', async () => {
  const { component, calls, updates } = setupMonitor();
  await component.onRefreshPress();
  await component.onMonitorPress([11, 12, 13, 99], true);
  const put = calls.find((call) => call.method === 'PUT');
  assert.equal(put.url, '/series/30/monitor');
  assert.deepEqual(JSON.parse(put.data), { bookIds: [11, 12], monitored: true });
  assert.equal(component.state.error, null);
  assert.equal(component.state.isSaving, false);
  assert.equal(updates.length, 2);
  assert.ok(component.state.books.every((book) => book.ebookMonitored));
});

test('a silently dropped monitor write is reported and never shown as saved', async () => {
  const { component } = setupMonitor({ ignoreWrite: true });
  await component.onRefreshPress();
  await component.onMonitorPress([11], true);
  assert.equal(component.state.error, 'SeriesMonitoringNotSaved');
  assert.equal(component.state.books.find((book) => book.id === 11).ebookMonitored, false);
});

test('failed series reads leave monitoring disabled with a visible error', async () => {
  const { component, calls } = setupMonitor({ failRead: true });
  await component.onRefreshPress();
  await component.onMonitorPress([11], true);
  assert.equal(component.state.error, 'SeriesBooksLoadFailed');
  assert.ok(calls.every((call) => call.method !== 'PUT'));
});
