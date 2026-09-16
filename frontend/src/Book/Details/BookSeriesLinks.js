import PropTypes from 'prop-types';
import React, { Component } from 'react';
import SeriesNameLink from 'Series/SeriesNameLink';
import createAjaxRequest from 'Utilities/createAjaxRequest';

class BookSeriesLinks extends Component {
  state = { series: [] };

  componentDidMount() {
    this.loadSeries();
  }

  componentDidUpdate(previous) {
    if (previous.bookId !== this.props.bookId ||
        previous.authorId !== this.props.authorId ||
        previous.mediaType !== this.props.mediaType) {
      this.loadSeries();
    }
  }

  componentWillUnmount() {
    this.requestVersion++;
    this.abortRequest?.();
  }

  requestVersion = 0;

  loadSeries = () => {
    const version = ++this.requestVersion;
    this.abortRequest?.();
    this.setState({ series: [] });

    const { authorId, bookId, mediaType } = this.props;
    if (!authorId || !bookId || !['ebook', 'audiobook'].includes(mediaType)) {
      return;
    }

    const { request, abortRequest } = createAjaxRequest({
      url: '/series',
      data: { authorId, mediaType },
      dataType: 'json'
    });
    this.abortRequest = abortRequest;

    request.done((items) => {
      if (version !== this.requestVersion) {
        return;
      }

      this.setState({
        series: items.filter((series) =>
          series.mediaType === mediaType &&
          (/^\d+$/).test(series.localSeriesId) &&
          series.links?.some((link) => link.bookId === bookId))
      });
    });
  };

  render() {
    const { bookId, seriesTitle } = this.props;
    const { series } = this.state;

    if (!series.length) {
      return seriesTitle || null;
    }

    return series.map((item, index) => {
      const position = item.links.find((link) => link.bookId === bookId)?.position;
      return (
        <React.Fragment key={item.localSeriesId}>
          {index > 0 ? '; ' : null}
          <SeriesNameLink
            localSeriesId={item.localSeriesId}
            seriesName={`${item.title}${position ? ` #${position}` : ''}`}
          />
        </React.Fragment>
      );
    });
  }
}

BookSeriesLinks.propTypes = {
  bookId: PropTypes.number,
  authorId: PropTypes.number,
  mediaType: PropTypes.string,
  seriesTitle: PropTypes.string
};

export default BookSeriesLinks;
