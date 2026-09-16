import PropTypes from 'prop-types';
import React from 'react';
import BookNameLink from 'Book/BookNameLink';
import Alert from 'Components/Alert';
import Label from 'Components/Label';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import MonitorToggleButton from 'Components/MonitorToggleButton';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageToolbar from 'Components/Page/Toolbar/PageToolbar';
import PageToolbarButton from 'Components/Page/Toolbar/PageToolbarButton';
import PageToolbarSection from 'Components/Page/Toolbar/PageToolbarSection';
import { icons, kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import styles from './SeriesDetails.css';

function SeriesDetails(props) {
  const { title, description, mediaType, books, isFetching, isSaving, error, onRefreshPress, onMonitorPress } = props;
  const field = mediaType === 'ebook' ? 'ebookMonitored' : 'audiobookMonitored';
  const allMonitored = books.length > 0 && books.every((book) => book[field]);
  const disabled = isFetching || isSaving;

  return (
    <PageContent title={title || translate('Series')}>
      <PageToolbar>
        <PageToolbarSection>
          <PageToolbarButton
            label={translate('Refresh')}
            iconName={icons.REFRESH}
            isSpinning={isFetching}
            isDisabled={isSaving}
            onPress={onRefreshPress}
          />
          <PageToolbarButton
            label={translate(allMonitored ? 'UnmonitorSeriesBooks' : 'MonitorSeriesBooks')}
            iconName={allMonitored ? icons.UNMONITORED : icons.MONITORED}
            isDisabled={disabled || !books.length}
            isSpinning={isSaving}
            onPress={() => onMonitorPress(books.map((book) => book.id), !allMonitored)}
          />
        </PageToolbarSection>
      </PageToolbar>
      <PageContentBody>
        <div className={styles.contentContainer}>
          <div className={styles.header}>
            <h1 className={styles.title}>{title}</h1>
            <div className={styles.details}>
              {mediaType ? <Label>{translate(mediaType === 'ebook' ? 'Ebooks' : 'Audiobooks')}</Label> : null}
              <Label>{books.length} {translate('Books')}</Label>
            </div>
          </div>
          {error ? <Alert kind={kinds.DANGER}>{error}</Alert> : null}
          {description ? <p className={styles.description}>{description}</p> : null}
          <div className={styles.books}>
            <h2>{translate('BooksInSeries')}</h2>
            <p>{translate('SeriesCurrentBooksMonitoringHelp')}</p>
            {isFetching ? <LoadingIndicator /> : null}
            {!isFetching && !books.length && !error ? <Alert>{translate('NoBooks')}</Alert> : null}
            {books.length ? (
              <ul className={styles.bookList}>
                {books.map((book) => (
                  <li key={book.id} className={styles.bookRow}>
                    <MonitorToggleButton
                      monitored={!!book[field]}
                      isSaving={isSaving}
                      isDisabled={disabled}
                      onPress={(monitored) => onMonitorPress([book.id], monitored)}
                    />
                    <span className={styles.position}>{book.position}</span>
                    <BookNameLink localBookId={String(book.id)} title={book.title} />
                  </li>
                ))}
              </ul>
            ) : null}
          </div>
        </div>
      </PageContentBody>
    </PageContent>
  );
}

SeriesDetails.propTypes = {
  title: PropTypes.string,
  description: PropTypes.string,
  mediaType: PropTypes.string,
  books: PropTypes.arrayOf(PropTypes.object).isRequired,
  isFetching: PropTypes.bool.isRequired,
  isSaving: PropTypes.bool.isRequired,
  error: PropTypes.string,
  onRefreshPress: PropTypes.func.isRequired,
  onMonitorPress: PropTypes.func.isRequired
};

export default SeriesDetails;
