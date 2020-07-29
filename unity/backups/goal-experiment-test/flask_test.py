from sqlalchemy import create_engine
from sqlalchemy.orm import scoped_session, sessionmaker
from sqlalchemy.ext.declarative import declarative_base
from sqlalchemy import Column, Integer, String, TIMESTAMP, func

from flask import Flask, render_template, jsonify, request, g
# using Flask-WTF CSRF protection for AJAX requests
from flask_wtf.csrf import CSRFProtect

app = Flask(__name__, static_folder="./", template_folder="./")
csrf = CSRFProtect(app)

# TODO: deal with actually reading a proper secret key file
app.secret_key = b'<\xf0\xa8\x99\xdb\xe5\xd1\xcd)\xd6\xfc-|z\xc8\xcc'

### SQL Alchemy Definitions
# The engine, db_session, and Base definitions would be in a db.py filter
# https://flask.palletsprojects.com/en/1.1.x/patterns/sqlalchemy/
engine = create_engine('sqlite:///./games.db', convert_unicode=True)
db_session = scoped_session(sessionmaker(autocommit=False,
                                         autoflush=False,
                                         bind=engine))
Base = declarative_base()
Base.query = db_session.query_property()

### SQL Aclhemy model
class Game(Base):
    __tablename__ = 'games'
    id = Column(Integer, primary_key=True)
    player_id = Column(String(32), unique=True)
    name = Column(String(64))
    description = Column(String(256))
    scoring = Column(String(256))
    timestamp = Column(TIMESTAMP(timezone=True),  server_default=func.now())

    def __init__(self, player_id=None, name=None, description=None,
                 scoring=None, timestamp=None, **kwargs):
        self.player_id = player_id
        self.name = name
        self.description = description
        self.scoring = scoring
        self.timestamp = timestamp


def init_db():
    # import all modules here that might define models so that
    # they will be registered properly on the metadata.  Otherwise
    # you will have to import them first before calling init_db()
    # import yourapplication.models
    Base.metadata.create_all(bind=engine)


init_db()


@app.teardown_appcontext
def shutdown_session(exception=None):
    db_session.remove()


@app.route('/')
def home():
    return render_template("index.html"), 200


@app.route('/save_game', methods=['POST'])
def save_game():
    # print('In save_game')
    # print(request.form)
    # print({key: request.form[key] for key in request.form})
    # print(dict(**request.form))
    game = Game(**request.form)
    query = Game.query.filter(Game.player_id == game.player_id)
    if query.count() > 0:
        game.id = query.first().id
        if game not in db_session:
            game = db_session.merge(game)

    else:
        db_session.add(game)

    db_session.commit()

    return jsonify({'id': game.id, 'player_id': game.player_id})


if __name__ == "__main__":
    port = int(os.environ.get("PORT", 5000))
    app.run(debug=True, host='0.0.0.0', port = port)
