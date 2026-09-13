import PropTypes from 'prop-types';
import React, { Component } from 'react';
import { connect } from 'react-redux';
import { updateItem } from 'Store/Actions/baseActions';
import createAjaxRequest from 'Utilities/createAjaxRequest';
import getErrorMessage from 'Utilities/Object/getErrorMessage';
import translate from 'Utilities/String/translate';
import SeriesDetails from './SeriesDetails';

class SeriesDetailsConnector extends Component {
  state = { series: {}, books: [], isFetching: true, isSaving: false, error: null };

  componentDidMount() {
    this.onRefreshPress();
  }

  componentDidUpdate(previous) {
    if (previous.localSeriesId !== this.props.localSeriesId) {
      this.onRefreshPress();
    }
  }

  componentWillUnmount() {
    this.cancelRequests();
  }

  requestVersion = 0;
  abortRequests = [];

  cancelRequests = () => {
    this.requestVersion++;
    this.abortRequests.forEach((abort) => abort());
    this.abortRequests = [];
  };

  request = (options) => {
    const { request, abortRequest } = createAjaxRequest({ dataType: 'json', ...options });
    this.abortRequests.push(abortRequest);
    request.always(() => {
      this.abortRequests = this.abortRequests.filter((abort) => abort !== abortRequest);
    });
    return request;
  };

  loadBooks = async(version) => {
    const series = await this.request({ url: `/series/${this.props.localSeriesId}` });
    if (version !== this.requestVersion) {
      return null;
    }

    const bookIds = [...new Set((series.links || []).map((link) => link.bookId))];
    const books = [];
    // Keep URLs bounded for long series. Never send an empty list: /book would
    // otherwise return the whole library.
    for (let offset = 0; offset < bookIds.length; offset += 100) {
      const batch = await this.request({
        url: '/book',
        data: { bookIds: bookIds.slice(offset, offset + 100) },
        traditional: true
      });
      if (version !== this.requestVersion) {
        return null;
      }
      books.push(...batch.filter((book) =>
        bookIds.includes(book.id) && book.mediaType === series.mediaType));
    }

    const positions = new Map((series.links || []).map((link) => [link.bookId, link.position || '']));
    const sortedBooks = books.map((book) => ({ ...book, position: positions.get(book.id) }))
      .sort((left, right) => left.position.localeCompare(right.position, undefined, { numeric: true }) || left.id - right.id);

    this.setState({ series, books: sortedBooks });
    return sortedBooks;
  };

  onRefreshPress = async() => {
    this.cancelRequests();
    const version = this.requestVersion;
    this.setState({ isFetching: true, isSaving: false, error: null, books: [] });
    try {
      await this.loadBooks(version);
    } catch (error) {
      if (version === this.requestVersion) {
        this.setState({ error: getErrorMessage(error, translate('SeriesBooksLoadFailed')) });
      }
    } finally {
      if (version === this.requestVersion) {
        this.setState({ isFetching: false });
      }
    }
  };

  onMonitorPress = async(bookIds, monitored) => {
    if (this.state.isFetching || this.state.isSaving) {
      return;
    }
    const allowedIds = new Set(this.state.books.map((book) => book.id));
    const selectedIds = [...new Set(bookIds)].filter((id) => allowedIds.has(id));
    if (!selectedIds.length) {
      return;
    }

    const version = this.requestVersion;
    this.setState({ isSaving: true, error: null });
    try {
      for (let offset = 0; offset < selectedIds.length; offset += 500) {
        await this.request({
          url: `/series/${this.props.localSeriesId}/monitor`,
          method: 'PUT',
          data: JSON.stringify({ bookIds: selectedIds.slice(offset, offset + 500), monitored })
        });
        if (version !== this.requestVersion) {
          return;
        }
      }
      if (version !== this.requestVersion) {
        return;
      }
      const books = await this.loadBooks(version);
      if (!books) {
        return;
      }
      books.forEach((book) => this.props.updateItem({ section: 'books', ...book }));
      const field = this.state.series.mediaType === 'ebook' ? 'ebookMonitored' : 'audiobookMonitored';
      if (selectedIds.some((id) => books.find((book) => book.id === id)?.[field] !== monitored)) {
        this.setState({ error: translate('SeriesMonitoringNotSaved') });
      }
    } catch (error) {
      if (version === this.requestVersion) {
        const message = getErrorMessage(error, translate('SeriesMonitoringFailed'));
        try {
          const books = await this.loadBooks(version);
          books?.forEach((book) => this.props.updateItem({ section: 'books', ...book }));
        } catch {
          // Keep the original mutation error visible; refresh remains available.
        }
        if (version === this.requestVersion) {
          this.setState({ error: message });
        }
      }
    } finally {
      if (version === this.requestVersion) {
        this.setState({ isSaving: false });
      }
    }
  };

  render() {
    return (
      <SeriesDetails
        {...this.state.series}
        books={this.state.books}
        isFetching={this.state.isFetching}
        isSaving={this.state.isSaving}
        error={this.state.error}
        onRefreshPress={this.onRefreshPress}
        onMonitorPress={this.onMonitorPress}
      />
    );
  }
}

SeriesDetailsConnector.propTypes = {
  localSeriesId: PropTypes.string.isRequired,
  updateItem: PropTypes.func.isRequired
};

export { SeriesDetailsConnector };
export default connect(null, { updateItem })(SeriesDetailsConnector);
